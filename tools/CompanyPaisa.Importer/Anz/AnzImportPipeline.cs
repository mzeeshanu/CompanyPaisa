using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Importer.Eu;
using CompanyPaisa.Importer.Pk;
using CompanyPaisa.Importer.Sec;
using CompanyPaisa.Importer.Uk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Anz;

/// <summary>
/// Builds the Australia / New Zealand market of the database from free sources — financials only:
/// ASX and NZX companies (Wikidata, Wikipedia) → each company's annual report found on its own website → revenue and
/// profit read from the statement of profit or loss (or the Appendix 4E summary) → headquarters from the LEI registry
/// (GLEIF), else the report's corporate directory, else Wikidata → postcode (GeoNames). The exchanges' own websites are
/// never used: their terms don't allow automated access. Companies without a findable, readable, recent report are left
/// out and listed in the report.
/// </summary>
public sealed partial class AnzImportPipeline(IOptions<ImporterOptions> options, RepoPaths paths, Publishing.DataPublisher publisher, ILoggerFactory loggers)
    : Publishing.IMarketImporter
{
    string Publishing.IMarketImporter.Market => Market;
    public string Description => "ASX and NZX: annual reports from companies' own websites (revenue and profit)";
    Task<int> Publishing.IMarketImporter.RunAsync(bool refreshLists, CancellationToken ct) => RunAsync(refreshLists, ct);

    /// <summary>This importer's partition of the database.</summary>
    public const string Market = "anz";

    private readonly AnzOptions _o = options.Value.Anz;
    private readonly ILogger _log = loggers.CreateLogger<AnzImportPipeline>();
    private readonly HaversineDistanceCalculator _distance = new();

    private sealed record ReadReport(ReportLink Link, AnzResults? Results, OfficeAddressText? Office, string? Problem);
    private sealed record Found(AnzListing Listing, List<ReadReport> Reports, string? Problem);

    public async Task<int> RunAsync(bool refreshLists, CancellationToken ct)
    {
        using var client = new SecClient(new SecOptions
        {
            UserAgent = _o.UserAgent, MaxRequestsPerSecond = _o.MaxRequestsPerSecond, CacheDirectory = _o.CacheDirectory,
            IndexCacheHours = _o.IndexCacheHours, CompressCache = true
        }, paths, loggers.CreateLogger<SecClient>());
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(4) };

        var postcodes = new EuPostcodes();
        await postcodes.LoadAsync(client, _o.PostcodesUrl, "AU", ct);
        await postcodes.LoadAsync(client, _o.PostcodesUrl, "NZ", ct);

        var listPath = paths.Resolve(_o.ListPath);
        var listings = refreshLists || !File.Exists(listPath) ? await AnzCompanies.FetchAsync(client, _o, ct) : AnzCompanies.Read(listPath);
        if (refreshLists || !File.Exists(listPath)) AnzCompanies.Write(listPath, listings);
        var only = _o.OnlyTickers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (only.Count > 0) listings = listings.Where(l => only.Contains(l.Ticker)).ToList();
        _log.LogInformation("Australia / New Zealand: {Count} listed companies to look for ({Asx} ASX, {Nzx} NZX)",
            listings.Count, listings.Count(l => l.Exchange == "ASX"), listings.Count(l => l.Exchange == "NZX"));

        // Companies the website already shows from another market (an ASX company that files with the SEC).
        var elsewhere = OtherMarkets();

        var results = new ConcurrentBag<Found>();
        var done = 0;
        await Parallel.ForEachAsync(listings, new ParallelOptions { MaxDegreeOfParallelism = _o.Parallelism, CancellationToken = ct }, async (l, token) =>
        {
            Found found;
            try { found = await ReadCompanyAsync(http, l, token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("{Ticker}: {Message}", l.Ticker, ex.Message);
                found = new Found(l, [], $"couldn't be read ({ex.Message})");
            }
            results.Add(found);
            var n = Interlocked.Increment(ref done);
            if (n % 25 == 0) _log.LogInformation("{Done}/{Total} companies read", n, listings.Count);
        });

        var companies = new List<Company>();
        var locations = new List<CompanyLocation>();
        var periods = new List<FinancialPeriod>();
        var included = new List<(string Country, string Region, string Line)>();
        var excluded = new List<string>();
        var warnings = new List<string>();
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-_o.RecentMonths);

        foreach (var f in results.OrderBy(r => r.Listing.Exchange).ThenBy(r => r.Listing.Ticker, StringComparer.Ordinal))
        {
            var l = f.Listing;
            var companyId = l.Ticker + (l.Exchange == "NZX" ? ".NZ" : ".AX");
            var label = $"{companyId} {l.Name}";
            if (elsewhere.GetValueOrDefault(NameKey(l.Name)) is { } other) { excluded.Add($"{label}: already shown from its {other}"); continue; }
            if (f.Problem is not null) { excluded.Add($"{label}: {f.Problem}"); continue; }
            var readable = f.Reports.Where(r => r.Results is not null).ToList();
            if (readable.Count == 0)
            {
                excluded.Add($"{label}: " + (f.Reports.Count == 0 ? "no annual report found on its website"
                    : $"no revenue and profit read from its report ({f.Reports[0].Problem ?? "layout not recognised"})"));
                continue;
            }
            var latest = readable.OrderByDescending(r => r.Results!.FiscalYear).First();
            var main = latest.Results!;
            var yearEnd = main.PeriodEnd ?? YearEnd(main.FiscalYear, l.Country);
            if (yearEnd < cutoff) { excluded.Add($"{label}: latest report found is for FY{main.FiscalYear} (not recent)"); continue; }

            // Where the company is.
            var place = await PlaceAsync(client, postcodes, l, readable.Select(r => r.Office).FirstOrDefault(o => o is not null), ct);
            if (place.Problem is not null) { excluded.Add($"{label}: {place.Problem}"); continue; }
            var (point, city, postcode, street, placeSource) = place.Value!.Value;

            // Years: the latest report's two, then each older report's (the same currency only).
            var years = new SortedDictionary<int, FinancialPeriod>();
            void Add(int year, decimal? revenue, decimal? netIncome, string src)
            {
                if (revenue is not { } rev || rev <= 0 || netIncome is not { } ni || years.ContainsKey(year)) return;
                years[year] = new FinancialPeriod { CompanyId = companyId, PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = rev, NetIncome = ni, SourceFiling = src };
            }
            Add(main.FiscalYear, main.Revenue, main.NetIncome, latest.Link.Url);
            Add(main.FiscalYear - 1, main.PriorRevenue, main.PriorNetIncome, latest.Link.Url);
            foreach (var older in readable.Where(r => r != latest && r.Results!.Currency == main.Currency && r.Results.FiscalYear < main.FiscalYear))
            {
                Add(older.Results!.FiscalYear, older.Results.Revenue, older.Results.NetIncome, older.Link.Url);
                Add(older.Results.FiscalYear - 1, older.Results.PriorRevenue, older.Results.PriorNetIncome, older.Link.Url);
            }
            var jumps = years.Values.Zip(years.Values.Skip(1)).Where(p => p.Second.Revenue > p.First.Revenue * 20 || p.First.Revenue > p.Second.Revenue * 20).ToList();
            if (jumps.Count > 0) warnings.Add($"{label}: revenue changes more than 20× between FY{jumps[0].First.FiscalYear} and FY{jumps[0].Second.FiscalYear} — check the unit");
            if (main.Source == "4E") warnings.Add($"{label}: figures from the Appendix 4E summary only (the statement wasn't read)");

            var region = RegionFor(l.Country, point);
            companies.Add(new Company
            {
                CompanyId = companyId, Ticker = companyId, Name = l.Name, Exchange = l.Exchange,
                Sector = SectorClassifier.FromIcb(l.Sector) is var s && s != "Other" ? s : SectorClassifier.FromIcb(Gics(l.Sector)),
                Industry = l.Sector, Website = Enrichment.WebsiteFinder.Tidy(l.Website), Currency = main.Currency,
                FiscalYearEnd = yearEnd.ToString("MM-dd", CultureInfo.InvariantCulture), AsOfDate = yearEnd
            });
            locations.Add(new CompanyLocation
            {
                LocationId = $"{companyId}-HQ", CompanyId = companyId, Type = LocationType.Headquarters, Label = "Headquarters",
                Street = street, City = city, State = l.Country, PostalCode = postcode, Point = point
            });
            periods.AddRange(years.Values);
            included.Add((l.Country == "NZ" ? "New Zealand" : "Australia", region, string.Create(CultureInfo.InvariantCulture,
                $"| {companyId} | {l.Name} | {city} ({placeSource}) | {Money(main.Revenue, main.Currency)} | {years.Keys.Min()}–{years.Keys.Max()} | [{(main.Source == "4E" ? "4E" : "report")}]({latest.Link.Url}) |")));
        }

        publisher.Publish(Market, companies, locations, periods, [], [], [],
            "Company list from Wikidata and Wikipedia; figures read from each company's annual report on its own website; headquarters from GLEIF, the reports or Wikidata; postcodes from GeoNames.",
            "Australia, New Zealand");
        await WriteReportAsync(listings.Count, companies.Count, periods.Count, included, excluded, warnings, ct);
        _log.LogInformation("Done: {Companies} Australian and New Zealand companies, {Periods} years of figures → {Path}",
            companies.Count, periods.Count, publisher.DatabasePath);
        return 0;
    }

    // ---- One company ---------------------------------------------------------------------------------------------

    private async Task<Found> ReadCompanyAsync(HttpClient http, AnzListing l, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(l.Website)) return new Found(l, [], "no website known (add one in " + _o.ListPath + ")");
        var links = await LinksAsync(http, l, ct);
        var home = l.Country == "NZ" ? "NZD" : "AUD";
        var reports = new List<ReadReport>();
        var readable = 0;
        foreach (var link in links.Take(_o.MaxReportTries + _o.ReportsPerCompany))
        {
            // After the latest readable report, only older years are worth reading.
            if (readable > 0 && link.Year >= reports.Where(r => r.Results is not null).Min(r => r.Results!.FiscalYear)) continue;
            var (lines, problem) = await ReportLinesAsync(http, link.Url, ct);
            AnzResults? results = null;
            OfficeAddressText? office = null;
            if (lines.Count > 0)
            {
                try { results = ResultsReader.Read(lines, home); office = OfficeAddress.Find(lines, l.Country); }
                catch (Exception ex) when (ex is not OperationCanceledException) { problem = $"reader failed: {ex.Message}"; }
                problem ??= results is null ? "layout not recognised" : null;
            }
            reports.Add(new ReadReport(link, results, office, problem));
            if (results is not null && ++readable >= _o.ReportsPerCompany) break;
            if (reports.Count >= _o.MaxReportTries && readable == 0) break;
        }
        return new Found(l, reports, null);
    }

    /// <summary>The website's report links, remembered for <see cref="AnzOptions.SiteCacheDays"/> (searching takes a dozen page reads).</summary>
    private async Task<List<ReportLink>> LinksAsync(HttpClient http, AnzListing l, CancellationToken ct)
    {
        var file = Path.Combine(paths.Resolve(_o.CacheDirectory), "sites", $"{l.Exchange}-{l.Ticker}.json");
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromDays(_o.SiteCacheDays))
            return JsonSerializer.Deserialize<List<ReportLink>>(await File.ReadAllTextAsync(file, ct)) ?? [];
        var links = await new ReportFinder(http).FindAsync(l.Website, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(links), ct);
        return links;
    }

    /// <summary>A report's text lines. Only the lines are cached (gzip), never the PDF; a failed download isn't cached.</summary>
    private async Task<(IReadOnlyList<PdfTextLine> Lines, string? Problem)> ReportLinesAsync(HttpClient http, string url, CancellationToken ct)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
        var file = Path.Combine(paths.Resolve(_o.CacheDirectory), "reports", $"{name}.lines.gz");
        if (File.Exists(file))
        {
            try
            {
                await using var input = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                using var reader = new StreamReader(input, Encoding.UTF8);
                var cached = new List<PdfTextLine>();
                while (await reader.ReadLineAsync(ct) is { } line) if (line.Length > 0) cached.Add(PdfTextLine.Deserialise(line));
                return (cached, cached.Count == 0 ? "the report is a scan or can't be read as text" : null);
            }
            catch (InvalidDataException) { File.Delete(file); }
        }

        byte[] pdf;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", ReportFinder.UserAgent);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return ([], $"the report link answered {(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength is > 0 and var size && size > _o.MaxReportMegabytes * 1_048_576L)
                return ([], $"the report is {size / 1_048_576} MB (over {_o.MaxReportMegabytes} MB)");
            pdf = await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return ([], $"the report didn't download ({ex.Message})");
        }
        if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P') return ([], "the report link isn't a PDF");
        var lines = PdfLines.Read(pdf);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var partial = file + ".partial";
        await using (var output = new GZipStream(File.Create(partial), CompressionLevel.SmallestSize))
        await using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
        {
            foreach (var line in lines) await writer.WriteLineAsync(line.Serialise().Replace('\n', ' ').Replace('\r', ' '));
            if (lines.Count == 0) await writer.WriteLineAsync();
        }
        File.Move(partial, file, overwrite: true);
        return (lines, lines.Count == 0 ? "the report is a scan or can't be read as text" : null);
    }

    // ---- Where it is ---------------------------------------------------------------------------------------------

    private async Task<((GeoPoint Point, string City, string Postcode, string Street, string Source)? Value, string? Problem)> PlaceAsync(
        ISecClient client, EuPostcodes postcodes, AnzListing l, OfficeAddressText? office, CancellationToken ct)
    {
        // 1. The LEI registry's headquarters address.
        if (l.Lei.Length == 20)
        {
            var (hq, legal) = await Gleif.GetAddressesAsync(client, _o.GleifApi, l.Lei, ct);
            var a = hq ?? legal;
            if (a is not null && a.Country is not ("AU" or "NZ")) return (null, $"headquartered outside Australia and New Zealand ({a.City}, {a.Country})");
            if (a is not null && postcodes.Locate(a.Country, a.PostalCode) is { } p)
                return ((p.Point, TitleCase(a.City.Length > 0 ? a.City : p.Place), EuPostcodes.Normalise(a.Country, a.PostalCode) ?? "", a.Street, "LEI"), null);
        }
        // 2. The corporate directory in its own report.
        if (office is not null && postcodes.Locate(l.Country, office.Postcode) is { } fromReport)
            return ((fromReport.Point, fromReport.Place, office.Postcode, office.Street, "report"), null);
        // 3. Wikidata's headquarters city, when it's in the country.
        if (l.HqLatitude is { } lat && l.HqLongitude is { } lng && postcodes.Nearest(l.Country, new GeoPoint(lat, lng)) is { } near)
            // Wikidata's headquarters can be a building ("120 Collins Street"): then the postcode's town names the city.
            return ((new GeoPoint(lat, lng), l.HqCity.Length > 0 && !l.HqCity.Any(char.IsDigit) ? l.HqCity : near.Place, near.Code,
                l.HqCity.Any(char.IsDigit) ? l.HqCity : "", "Wikidata"), null);
        return (null, l.HqCity.Length > 0 ? $"headquarters ({l.HqCity}) not in {(l.Country == "NZ" ? "New Zealand" : "Australia")} or not placed" : "no address found");
    }

    /// <summary>Normalised names of companies other markets publish ("resmed" → "its US filings (RMD)").</summary>
    private Dictionary<string, string> OtherMarkets()
    {
        var result = new Dictionary<string, string>();
        if (!File.Exists(publisher.DatabasePath)) return result;
        var data = CompanyPaisa.Data.Sqlite.SqliteDataStore.Read(publisher.DatabasePath, DateTimeOffset.UtcNow);
        foreach (var c in data.Companies.Where(c => !c.Ticker.EndsWith(".AX", StringComparison.Ordinal) && !c.Ticker.EndsWith(".NZ", StringComparison.Ordinal)))
            result.TryAdd(NameKey(c.Name), $"{(c.Ticker.Contains('.') ? "home market" : "US filings")} ({c.Ticker})");
        return result;
    }

    /// <summary>"BHP Group Limited" → "bhp"; "Fletcher Building Ltd" → "fletcherbuilding".</summary>
    internal static string NameKey(string name)
    {
        var words = Regex.Matches(name.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value)
            .Where(w => w is not ("the" or "limited" or "ltd" or "inc" or "corp" or "corporation" or "plc" or "group" or "holdings" or "holding" or "co" or "nl" or "company" or "and"));
        return string.Concat(words);
    }

    /// <summary>A GICS name or Wikidata industry the ICB keywords don't cover, in their words.</summary>
    private static string Gics(string s) => s.ToLowerInvariant() switch
    {
        var x when x.Contains("financials") || x.Contains("banks") || x.Contains("diversified financ") => "financ",
        var x when x.Contains("communication") => "telecom",
        var x when x.Contains("information technology") => "software",
        var x when x.Contains("consumer staples") || x.Contains("consumer discretionary") => "consumer",
        var x when x.Contains("health care") => "health",
        var x when x.Contains("capital goods") || x.Contains("commercial") => "industrial",
        var x when x.Contains("gold") || x.Contains("lithium") || x.Contains("copper") || x.Contains("iron") || x.Contains("mineral") || x.Contains("exploration") => "mining",
        var x => x
    };

    private string RegionFor(string country, GeoPoint point)
    {
        var mine = _o.Regions.Where(r => r.Country.Equals(country, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var r in mine)
            if (r.Anchors.Any(a => _distance.DistanceMiles(point, new GeoPoint(a.Latitude, a.Longitude)) <= a.RadiusMiles)) return r.Name;
        return mine.FirstOrDefault(r => r.WholeCountry)?.Name ?? (country == "NZ" ? "New Zealand" : "Australia");
    }

    /// <summary>Without a printed date: 30 June for Australia, 31 March for New Zealand (the usual year ends).</summary>
    private static DateOnly YearEnd(int fiscalYear, string country) => country == "NZ" ? new DateOnly(fiscalYear, 3, 31) : new DateOnly(fiscalYear, 6, 30);

    private static string TitleCase(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower) ? s.Trim() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    private static string Money(decimal v, string currency)
    {
        var sym = currency switch { "AUD" => "A$", "NZD" => "NZ$", "USD" => "US$", _ => currency + " " };
        return v >= 1_000_000_000 ? $"{sym}{v / 1_000_000_000:0.00}B" : v >= 1_000_000 ? $"{sym}{v / 1_000_000:0.0}M" : $"{sym}{v / 1_000:0}K";
    }

    private async Task WriteReportAsync(int listed, int companies, int periods, List<(string Country, string Region, string Line)> included,
        List<string> excluded, List<string> warnings, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Australia / New Zealand import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("Scope: ASX and NZX companies known to Wikidata or in Wikipedia's S&P/ASX 200 and NZX tables, whose latest annual report " +
                      "could be found on their own website and read. Financials only. The exchanges' own websites aren't used (their terms " +
                      "don't allow automated access). Sources: Wikidata, Wikipedia, companies' websites, GLEIF, GeoNames.").AppendLine();
        sb.AppendLine($"- Companies looked for: {listed}");
        sb.AppendLine($"- Companies included: **{companies}**");
        sb.AppendLine($"- Years of figures: {periods}").AppendLine();
        sb.AppendLine("| Country | Area | Companies |").AppendLine("|---|---|---|");
        foreach (var g in included.GroupBy(i => (i.Country, i.Region)).OrderBy(g => g.Key.Country).ThenByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key.Country} | {g.Key.Region} | {g.Count()} |");
        foreach (var g in included.GroupBy(i => i.Country).OrderBy(g => g.Key))
        {
            sb.AppendLine().AppendLine($"## Included — {g.Key} ({g.Count()})").AppendLine();
            sb.AppendLine("| Ticker | Company | City (placed from) | Latest revenue | Years | Source |").AppendLine("|---|---|---|---|---|---|");
            foreach (var line in g.Select(i => i.Line).Order(StringComparer.Ordinal)) sb.AppendLine(line);
        }
        sb.AppendLine().AppendLine($"## Excluded ({excluded.Count})").AppendLine();
        foreach (var grp in excluded.GroupBy(e => Regex.Replace(e[(e.IndexOf(": ", StringComparison.Ordinal) + 2)..], @"\(.*\)|FY\d{4}", "…")).OrderByDescending(g => g.Count()))
            sb.AppendLine($"- {grp.Key}: {grp.Count()}");
        sb.AppendLine();
        foreach (var line in excluded.Order(StringComparer.Ordinal)) sb.AppendLine($"- {line}");
        sb.AppendLine().AppendLine("## Needs review").AppendLine();
        foreach (var line in warnings) sb.AppendLine($"- {line}");
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }
}
