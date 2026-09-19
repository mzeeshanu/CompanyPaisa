using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>Settings for --enrich (appsettings section "Importer:Enrichment"). Paths are relative to the repo root.</summary>
public sealed class EnrichmentOptions
{
    public string SitesPath { get; set; } = "data/reference/company-sites.csv";
    public string PointsPath { get; set; } = "data/reference/geocoded-locations.csv";
    public string ReportPath { get; set; } = "data/import-report-enrichment.md";
    public string CacheDirectory { get; set; } = "data/cache/enrich";
    public string WikidataSparql { get; set; } = "https://query.wikidata.org/sparql";
    /// <summary>Look for a careers page again after this many days (sites change; a missing page may appear).</summary>
    [Range(1, 3650)] public int CareersRecheckDays { get; set; } = 90;
    /// <summary>Company websites visited at the same time (each site is only visited by one worker).</summary>
    [Range(1, 32)] public int CareersParallelism { get; set; } = 12;
}

/// <summary>
/// --enrich: fills in what the markets' own sources don't give — each company's website and careers page, and the exact
/// position of its street address — for every company in the database, whatever its market. Results go into two
/// reviewable tables in data/reference (so a monthly refresh keeps them) and are applied to the database straight away.
/// Steps: websites, careers, geocode (all by default).
/// </summary>
public sealed class EnrichmentRun(IOptions<ImporterOptions> options, RepoPaths paths, IEdgarService edgar, ILoggerFactory loggers)
{
    private readonly EnrichmentOptions _o = options.Value.Enrichment;
    private readonly ILogger _log = loggers.CreateLogger<EnrichmentRun>();

    public async Task<int> RunAsync(IReadOnlyCollection<string> steps, CancellationToken ct)
    {
        bool Step(string s) => steps.Count == 0 || steps.Contains(s, StringComparer.OrdinalIgnoreCase);
        var dbPath = paths.Resolve(options.Value.Output.DatabasePath);
        SqliteDataStore.Upgrade(dbPath);
        var data = SqliteDataStore.Read(dbPath, DateTimeOffset.UtcNow);
        var sites = EnrichmentTables.ReadSites(paths.Resolve(_o.SitesPath));
        var points = EnrichmentTables.ReadPoints(paths.Resolve(_o.PointsPath));
        var notes = new List<string>();

        using var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, MaxAutomaticRedirections = 6,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = TimeSpan.FromSeconds(20) };

        if (Step("websites")) await WebsitesAsync(data.Companies, sites, notes, ct);
        EnrichmentTables.WriteSites(paths.Resolve(_o.SitesPath), sites.Values);
        if (Step("careers")) await CareersAsync(http, sites, ct);
        EnrichmentTables.WriteSites(paths.Resolve(_o.SitesPath), sites.Values);
        if (Step("geocode")) await GeocodeAsync(http, data.Companies, data.Locations, points, ct);
        EnrichmentTables.WritePoints(paths.Resolve(_o.PointsPath), points.Values);

        // Straight into the database, for every market (imports apply the same tables when they publish).
        var byLocation = data.Locations.ToDictionary(l => l.LocationId, StringComparer.OrdinalIgnoreCase);
        SqliteDataStore.ApplyCompanyDetails(dbPath,
            sites.Values.Where(s => s.Website is not null || s.CareersUrl is not null).Select(s => (s.CompanyId, s.Website, s.CareersUrl)).ToList(),
            points.Values.Where(p => byLocation.TryGetValue(p.LocationId, out var l) && EnrichmentTables.AddressOf(l) == p.Address)
                .Select(p => (p.LocationId, p.Latitude, p.Longitude)).ToList());
        await WriteReportAsync(data.Companies, data.Locations, sites, points, notes, ct);
        _log.LogInformation("Done: {Websites} websites, {Careers} careers pages, {Points} street positions → {Path}",
            sites.Values.Count(s => s.Website is not null), sites.Values.Count(s => s.CareersUrl is not null), points.Count, dbPath);
        return 0;
    }

    // ---- Websites --------------------------------------------------------------------------------------------------

    private async Task WebsitesAsync(IReadOnlyList<Company> companies, Dictionary<string, CompanySite> sites, List<string> notes, CancellationToken ct)
    {
        using var wikidataClient = new SecClient(new SecOptions
        {
            UserAgent = CareersFinder.UserAgent, MaxRequestsPerSecond = 1, CacheDirectory = _o.CacheDirectory, IndexCacheHours = 24 * 7, CompressCache = true
        }, paths, loggers.CreateLogger<SecClient>());
        var finder = new WebsiteFinder(wikidataClient, _o.WikidataSparql);
        await finder.LoadAsync(ct);
        var leis = CuratedLeis();
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-3);
        int fromWikidata = 0, fromFilings = 0, done = 0;

        foreach (var c in companies)
        {
            ct.ThrowIfCancellationRequested();
            if (++done % 500 == 0) _log.LogInformation("Websites: {Done}/{Total} companies", done, companies.Count);
            var existing = sites.GetValueOrDefault(c.CompanyId);
            if (existing?.Website is not null) continue;
            string? site = null, source = null;
            if (WebsiteFinder.Tidy(c.Website) is { } own) (site, source) = (own, "exchange profile");
            var market = MarketOf(c);
            long? cik = null;
            if (site is null && market == "sec") cik = await edgar.FindCikByTickerAsync(c.Ticker, ct);
            if (site is null && finder.FromWikidata(cik, leis.GetValueOrDefault(c.Ticker), c.Ticker) is { } wd) { (site, source) = (wd, "wikidata"); fromWikidata++; }
            if (site is null && cik is { } k && await FromFilingsAsync(k, c, since, ct) is { } fs) { (site, source) = (fs, "sec filing"); fromFilings++; }
            sites[c.CompanyId] = new CompanySite(c.CompanyId, site, source, existing?.CareersUrl, existing?.CareersCheckedOn);
        }
        notes.Add($"Websites this run: {fromWikidata} from Wikidata, {fromFilings} from the companies' own SEC filings.");
    }

    /// <summary>The website a US filer names in its latest proxy statement or annual report.</summary>
    private async Task<string?> FromFilingsAsync(long cik, Company c, DateOnly since, CancellationToken ct)
    {
        var sec = await edgar.GetCompanyAsync(cik, since, ct);
        if (sec is null) return null;
        foreach (var form in new[] { "DEF 14A", "10-K", "20-F", "40-F" })
        {
            var filing = sec.Filings.Where(f => f.Form == form).MaxBy(f => f.FilingDate);
            if (filing is null) continue;
            var html = await edgar.GetDocumentAsync(filing.Url(cik), ct);
            if (html is not null && WebsiteFinder.FromFilingText(html, c.Name, c.Ticker) is { } site) return site;
        }
        return null;
    }

    /// <summary>ticker → LEI from the curated UK and European lists.</summary>
    private Dictionary<string, string> CuratedLeis()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, suffixes) in new[] { ("data/curated/uk-main-market.csv", new[] { ".L" }), ("data/curated/eu-companies.csv", new[] { ".PA", ".AS", ".MI", ".MC" }) })
        {
            var rows = Csv.Read(paths.Resolve(file));
            if (rows.Count == 0) continue;
            var ti = rows[0].FindIndex(h => h.Equals("ticker", StringComparison.OrdinalIgnoreCase));
            var li = rows[0].FindIndex(h => h.Equals("lei", StringComparison.OrdinalIgnoreCase));
            if (ti < 0 || li < 0) continue;
            foreach (var r in rows.Skip(1).Where(r => r.Count > Math.Max(ti, li) && r[li].Length > 0))
                foreach (var s in suffixes) map[r[ti] + s] = r[li];
        }
        return map;
    }

    // ---- Careers pages ---------------------------------------------------------------------------------------------

    private async Task CareersAsync(HttpClient http, Dictionary<string, CompanySite> sites, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = sites.Values.Where(s => s.Website is not null &&
            (s.CareersCheckedOn is null || s.CareersCheckedOn < today.AddDays(-_o.CareersRecheckDays))).ToList();
        _log.LogInformation("Careers pages: {Count} websites to visit", due.Count);
        var finder = new CareersFinder(http);
        var results = new ConcurrentDictionary<string, CompanySite>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        // One worker per website host, so no site gets more than one visitor at a time.
        await Parallel.ForEachAsync(due.GroupBy(s => new Uri(s.Website!).Host), new ParallelOptions { MaxDegreeOfParallelism = _o.CareersParallelism, CancellationToken = ct },
            async (group, token) =>
            {
                foreach (var s in group)
                {
                    string? careers = null;
                    try { careers = await finder.FindAsync(s.Website!, token); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // One odd website (a broken redirect, a strange encoding) mustn't stop the run.
                        _log.LogDebug("Careers page for {Company} ({Site}) failed: {Message}", s.CompanyId, s.Website, ex.Message);
                    }
                    results[s.CompanyId] = s with { CareersUrl = careers ?? s.CareersUrl, CareersCheckedOn = today };
                    var n = Interlocked.Increment(ref done);
                    if (n % 250 == 0)
                    {
                        _log.LogInformation("Careers pages: {Done}/{Total} visited, {Found} found", n, due.Count, results.Values.Count(r => r.CareersUrl is not null));
                        SaveProgress(sites, results);
                    }
                }
            });
        foreach (var (id, s) in results) sites[id] = s;
    }

    private readonly Lock _saveLock = new();

    /// <summary>Writes what's been found so far, so a stopped run carries on where it left off.</summary>
    private void SaveProgress(Dictionary<string, CompanySite> sites, ConcurrentDictionary<string, CompanySite> results)
    {
        lock (_saveLock)
        {
            var merged = new Dictionary<string, CompanySite>(sites, StringComparer.OrdinalIgnoreCase);
            foreach (var (id, s) in results) merged[id] = s;
            EnrichmentTables.WriteSites(paths.Resolve(_o.SitesPath), merged.Values);
        }
    }

    // ---- Street positions ------------------------------------------------------------------------------------------

    private async Task GeocodeAsync(HttpClient http, IReadOnlyList<Company> companies, IReadOnlyList<CompanyLocation> locations,
        Dictionary<string, GeocodedLocation> points, CancellationToken ct)
    {
        var byId = companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var misses = MissesFile();
        var tried = File.Exists(misses) ? File.ReadAllLines(misses).ToHashSet(StringComparer.Ordinal) : [];
        var todo = locations
            .Where(l => l.Street.Length > 0 && !(points.TryGetValue(l.LocationId, out var p) && p.Address == EnrichmentTables.AddressOf(l)))
            .Where(l => !tried.Contains(EnrichmentTables.AddressOf(l)))
            .Select(l => new AddressToPlace(l.LocationId, CountryOf(l, byId.GetValueOrDefault(l.CompanyId)), l.Street, l.City, l.State, l.PostalCode, l.Point))
            .ToList();
        _log.LogInformation("Street positions: {Count} addresses to place ({Us} in the US)", todo.Count, todo.Count(t => t.Country == "US"));
        var placed = await new StreetGeocoder(http, _log).PlaceAsync(todo, ct);
        var byLocation = locations.ToDictionary(l => l.LocationId, StringComparer.OrdinalIgnoreCase);
        foreach (var (a, point, source) in placed)
            points[a.LocationId] = new GeocodedLocation(a.LocationId, EnrichmentTables.AddressOf(byLocation[a.LocationId]), point.Latitude, point.Longitude, source);
        // Addresses no geocoder could place aren't sent again on the next run.
        var placedIds = placed.Select(p => p.Address.LocationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.GetDirectoryName(misses)!);
        File.AppendAllLines(misses, todo.Where(t => !placedIds.Contains(t.LocationId)).Select(t => EnrichmentTables.AddressOf(byLocation[t.LocationId])));
    }

    private string MissesFile() => Path.Combine(paths.Resolve(_o.CacheDirectory), "geocode-misses.txt");

    private static readonly HashSet<string> CanadianProvinces = ["AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"];

    /// <summary>ISO country of a location: European, Australian, NZ and Pakistani locations store it; US/Canadian ones a state.</summary>
    internal static string CountryOf(CompanyLocation l, Company? c)
    {
        var s = l.State.ToUpperInvariant();
        if (c is not null && MarketOf(c) is "eu" or "pk") return s;
        return s switch
        {
            "UK" or "GB" => "GB",
            "AU" or "NZ" => s,
            _ when CanadianProvinces.Contains(s) => "CA",
            _ => "US"
        };
    }

    internal static string MarketOf(Company c)
    {
        var t = c.Ticker.ToUpperInvariant();
        if (t.EndsWith(".L", StringComparison.Ordinal)) return "uk";
        if (t.EndsWith(".KA", StringComparison.Ordinal)) return "pk";
        return t.EndsWith(".PA", StringComparison.Ordinal) || t.EndsWith(".AS", StringComparison.Ordinal) || t.EndsWith(".MI", StringComparison.Ordinal) || t.EndsWith(".MC", StringComparison.Ordinal)
            ? "eu" : "sec";
    }

    // ---- Report ----------------------------------------------------------------------------------------------------

    private async Task WriteReportAsync(IReadOnlyList<Company> companies, IReadOnlyList<CompanyLocation> locations,
        Dictionary<string, CompanySite> sites, Dictionary<string, GeocodedLocation> points, List<string> notes, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Company details report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("Websites (Wikidata, the exchange's profile, or the company's own SEC filings), careers pages (the link on the company's " +
                      "home page), and street positions (US Census Bureau geocoder in the US, OpenStreetMap elsewhere; kept only within " +
                      $"{StreetGeocoder.MaxMilesFromPostcode} miles of the postcode). Tables: `{_o.SitesPath}`, `{_o.PointsPath}`.").AppendLine();
        foreach (var n in notes) sb.AppendLine($"- {n}");
        sb.AppendLine().AppendLine("| Market | Companies | Website | Careers page | Headquarters at street address |").AppendLine("|---|---|---|---|---|");
        var hq = locations.Where(l => l.IsHeadquarters).GroupBy(l => l.CompanyId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var g in companies.GroupBy(MarketOf).OrderBy(g => g.Key))
        {
            int web = 0, careers = 0, street = 0;
            foreach (var c in g)
            {
                if (sites.GetValueOrDefault(c.CompanyId) is { } s) { if (s.Website is not null) web++; if (s.CareersUrl is not null) careers++; }
                if (hq.TryGetValue(c.CompanyId, out var l) && points.TryGetValue(l.LocationId, out var p) && p.Address == EnrichmentTables.AddressOf(l)) street++;
            }
            sb.AppendLine($"| {g.Key} | {g.Count()} | {web} | {careers} | {street} |");
        }
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }
}
