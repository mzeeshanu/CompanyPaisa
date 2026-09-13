using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Sec;

/// <summary>Everything the importer needs from EDGAR.</summary>
public interface IEdgarService
{
    /// <summary>CIKs of companies whose business address is in one of the configured states and that filed recently.</summary>
    Task<IReadOnlyList<long>> DiscoverFilerCiksAsync(CancellationToken ct);

    /// <summary>Everyone who has filed insider-ownership reports (Forms 3/4/5) for the company, with their own SEC CIK.</summary>
    Task<IReadOnlyList<SecInsider>> GetInsidersAsync(long cik, CancellationToken ct);

    /// <summary>CIK for a ticker (for curated companies headquartered elsewhere).</summary>
    Task<long?> FindCikByTickerAsync(string ticker, CancellationToken ct);

    /// <summary>Company record plus filings reaching back to <paramref name="filingsSince"/>.</summary>
    Task<SecCompany?> GetCompanyAsync(long cik, DateOnly filingsSince, CancellationToken ct);

    /// <summary>XBRL company facts JSON (revenue, net income…), or null if the company has none.</summary>
    Task<string?> GetCompanyFactsAsync(long cik, CancellationToken ct);

    Task<string?> GetDocumentAsync(string url, CancellationToken ct);
}

public sealed class EdgarService(ISecClient client, IOptions<ImporterOptions> options, ILogger<EdgarService> logger) : IEdgarService
{
    private Dictionary<string, long>? _tickers;

    public async Task<IReadOnlyList<long>> DiscoverFilerCiksAsync(CancellationToken ct)
    {
        var d = options.Value.Discovery;
        var all = new SortedSet<long>();
        var forms = string.Join(",", d.Forms.Count > 0 ? d.Forms : ["10-K"]);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var state in d.States.Select(s => s.Trim().ToUpperInvariant()).Distinct())
        {
            var ciks = new HashSet<long>();
            await SearchAsync(state, forms, d.FiledSince, today, ciks, ct);
            logger.LogInformation("Discovered {Count} {State} filers of {Forms} since {Since}", ciks.Count, state, forms, d.FiledSince);
            all.UnionWith(ciks);
        }
        return all.ToList();
    }

    private const int SearchPageSize = 100;

    /// <summary>
    /// Deep paging through EDGAR full-text search isn't guaranteed to be stable, so rather than page, halve the
    /// date range until each query fits on one page. Only a single day with more than a page of filings is paged.
    /// </summary>
    private async Task SearchAsync(string state, string forms, DateOnly start, DateOnly end, HashSet<long> ciks, CancellationToken ct)
    {
        var (total, first) = await SearchPageAsync(state, forms, start, end, 0, ct);
        if (total <= SearchPageSize) { ciks.UnionWith(first); return; }

        if (start < end)
        {
            var mid = start.AddDays((end.DayNumber - start.DayNumber) / 2);
            await SearchAsync(state, forms, start, mid, ciks, ct);
            await SearchAsync(state, forms, mid.AddDays(1), end, ciks, ct);
            return;
        }

        ciks.UnionWith(first);
        for (var from = SearchPageSize; from < Math.Min(total, 10_000); from += SearchPageSize)
            ciks.UnionWith((await SearchPageAsync(state, forms, start, end, from, ct)).Ciks);
        logger.LogWarning("{State} {Day}: {Total} filings in one day — paged, a few filers may be missed", state, start, total);
    }

    private async Task<(int Total, List<long> Ciks)> SearchPageAsync(string state, string forms, DateOnly start, DateOnly end, int from, CancellationToken ct)
    {
        var url = $"https://efts.sec.gov/LATEST/search-index?forms={Uri.EscapeDataString(forms)}&locationCodes={state}" +
                  $"&dateRange=custom&startdt={start:yyyy-MM-dd}&enddt={end:yyyy-MM-dd}&from={from}";
        var json = await client.GetStringAsync(url, CachePolicy.Index, ct)
                   ?? throw new InvalidOperationException($"EDGAR search failed for {state} {start}–{end}; re-run the importer.");

        using var doc = JsonDocument.Parse(json);
        var hits = doc.RootElement.GetProperty("hits");
        var ciks = new List<long>();
        foreach (var hit in hits.GetProperty("hits").EnumerateArray())
            foreach (var c in hit.GetProperty("_source").GetProperty("ciks").EnumerateArray())
                if (long.TryParse(c.GetString(), out var cik)) ciks.Add(cik);
        return (hits.GetProperty("total").GetProperty("value").GetInt32(), ciks);
    }

    public async Task<IReadOnlyList<SecInsider>> GetInsidersAsync(long cik, CancellationToken ct)
    {
        var html = await client.GetStringAsync($"https://www.sec.gov/cgi-bin/own-disp?action=getissuer&CIK={cik:D10}", CachePolicy.Index, ct);
        return html is null ? [] : InsiderParser.Parse(html);
    }

    public async Task<long?> FindCikByTickerAsync(string ticker, CancellationToken ct)
    {
        await LoadTickersAsync(ct);
        return _tickers!.TryGetValue(ticker, out var cik) ? cik : null;
    }

    /// <summary>The SEC's ticker file: ticker → CIK, and each CIK's primary (first-listed) ticker and exchange.</summary>
    private async Task LoadTickersAsync(CancellationToken ct)
    {
        if (_tickers is not null) return;
        var json = await client.GetStringAsync("https://www.sec.gov/files/company_tickers_exchange.json", CachePolicy.Index, ct) ?? "{}";
        using var doc = JsonDocument.Parse(json);
        _tickers = new(StringComparer.OrdinalIgnoreCase);
        _listings = [];
        if (doc.RootElement.TryGetProperty("data", out var data))
            foreach (var row in data.EnumerateArray())   // [cik, name, ticker, exchange]
            {
                var (cik, ticker, exchange) = (row[0].GetInt64(), row[2].GetString() ?? "", row[3].ValueKind == JsonValueKind.String ? row[3].GetString() ?? "" : "");
                _tickers.TryAdd(ticker, cik);
                _listings.TryAdd(cik, (ticker, exchange));
            }
    }

    private Dictionary<long, (string Ticker, string Exchange)>? _listings;

    public async Task<SecCompany?> GetCompanyAsync(long cik, DateOnly filingsSince, CancellationToken ct)
    {
        var json = await client.GetStringAsync($"https://data.sec.gov/submissions/CIK{cik:D10}.json", CachePolicy.Index, ct);
        if (json is null) return null;

        var (company, olderPages) = SubmissionsParser.Parse(cik, json);
        var filings = company.Filings.ToList();

        // Large filers push old proxies out of "recent"; pull older pages until we reach the history window.
        foreach (var page in olderPages)
        {
            if (filings.Count > 0 && filings.Min(f => f.FilingDate) <= filingsSince) break;
            var pageJson = await client.GetStringAsync($"https://data.sec.gov/submissions/{page}", CachePolicy.Immutable, ct);
            if (pageJson is not null) filings.AddRange(SubmissionsParser.ParseFilingsPage(pageJson));
        }

        company = company with { Filings = filings.OrderByDescending(f => f.FilingDate).ToList() };

        // A submissions record can come back with no tickers while the SEC's ticker file still lists the company.
        if (company.PrimaryTicker is null)
        {
            await LoadTickersAsync(ct);
            if (_listings!.TryGetValue(cik, out var listing))
                company = company with { Tickers = [listing.Ticker], Exchanges = [listing.Exchange] };
        }
        return company;
    }

    public Task<string?> GetCompanyFactsAsync(long cik, CancellationToken ct) =>
        client.GetStringAsync($"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik.ToString("D10", CultureInfo.InvariantCulture)}.json", CachePolicy.Index, ct);

    public Task<string?> GetDocumentAsync(string url, CancellationToken ct) => client.GetStringAsync(url, CachePolicy.Immutable, ct);
}
