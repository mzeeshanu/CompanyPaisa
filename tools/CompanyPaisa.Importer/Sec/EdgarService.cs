using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Sec;

/// <summary>Everything the importer needs from EDGAR.</summary>
public interface IEdgarService
{
    /// <summary>CIKs of companies whose business address is in the configured state and that filed recently.</summary>
    Task<IReadOnlyList<long>> DiscoverFilerCiksAsync(CancellationToken ct);

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
        var ciks = new SortedSet<long>();
        var forms = string.Join(",", d.Forms.Count > 0 ? d.Forms : ["10-K"]);
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        for (var from = 0; ; from += 100)
        {
            var url = $"https://efts.sec.gov/LATEST/search-index?forms={Uri.EscapeDataString(forms)}&locationCodes={d.State}" +
                      $"&dateRange=custom&startdt={d.FiledSince:yyyy-MM-dd}&enddt={today}&from={from}";
            var json = await client.GetStringAsync(url, CachePolicy.Index, ct);
            if (json is null) break;

            using var doc = JsonDocument.Parse(json);
            var hits = doc.RootElement.GetProperty("hits");
            var total = hits.GetProperty("total").GetProperty("value").GetInt32();
            var page = hits.GetProperty("hits").EnumerateArray().ToList();
            foreach (var hit in page)
                foreach (var c in hit.GetProperty("_source").GetProperty("ciks").EnumerateArray())
                    if (long.TryParse(c.GetString(), out var cik)) ciks.Add(cik);

            if (page.Count == 0 || from + 100 >= total || from >= 9_900) break;   // EDGAR search caps at 10,000 hits
        }

        logger.LogInformation("Discovered {Count} {State} filers of {Forms} since {Since}", ciks.Count, d.State, forms, d.FiledSince);
        return ciks.ToList();
    }

    public async Task<long?> FindCikByTickerAsync(string ticker, CancellationToken ct)
    {
        if (_tickers is null)
        {
            var json = await client.GetStringAsync("https://www.sec.gov/files/company_tickers_exchange.json", CachePolicy.Index, ct) ?? "{}";
            using var doc = JsonDocument.Parse(json);
            _tickers = new(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var row in data.EnumerateArray())
                    _tickers.TryAdd(row[2].GetString() ?? "", row[0].GetInt64());
        }
        return _tickers.TryGetValue(ticker, out var cik) ? cik : null;
    }

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

        return company with { Filings = filings.OrderByDescending(f => f.FilingDate).ToList() };
    }

    public Task<string?> GetCompanyFactsAsync(long cik, CancellationToken ct) =>
        client.GetStringAsync($"https://data.sec.gov/api/xbrl/companyfacts/CIK{cik.ToString("D10", CultureInfo.InvariantCulture)}.json", CachePolicy.Index, ct);

    public Task<string?> GetDocumentAsync(string url, CancellationToken ct) => client.GetStringAsync(url, CachePolicy.Immutable, ct);
}
