using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Importer.Compensation;
using CompanyPaisa.Importer.Financials;
using CompanyPaisa.Importer.Geo;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer;

/// <summary>
/// Builds the real CompanyPaisa workbook from SEC EDGAR:
/// discover Utah filers → keep listed companies in the region (+ curated offices) → XBRL financials →
/// proxy-statement executive pay → write workbook, ZIP table and a review report.
/// </summary>
public sealed partial class ImportPipeline(
    IEdgarService edgar,
    IFinancialsExtractor financials,
    ICompensationParser compensation,
    IZipGeocoder geo,
    ISecClient client,
    IOptions<ImporterOptions> options,
    RepoPaths paths,
    ILogger<ImportPipeline> logger)
{
    private sealed record Candidate(SecCompany Sec, CompanyLocation Location, bool FromCurated, string? Note);

    private sealed class Outcome
    {
        public List<Company> Companies { get; } = [];
        public List<CompanyLocation> Locations { get; } = [];
        public List<FinancialPeriod> Financials { get; } = [];
        public List<ExecutiveCompensation> Pay { get; } = [];
        public Dictionary<string, Person> People { get; } = new();
        public List<string> Included { get; } = [];
        public List<string> Excluded { get; } = [];
        public List<string> Warnings { get; } = [];
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var historyStart = new DateOnly(DateTime.UtcNow.Year - o.History.Years - 1, 1, 1);
        var outcome = new Outcome();
        await geo.LoadAsync(ct);

        // 1. Utah filers from EDGAR full-text search, filtered to the region and to listed companies.
        var candidates = new Dictionary<long, Candidate>();
        foreach (var cik in await edgar.DiscoverFilerCiksAsync(ct))
        {
            var sec = await edgar.GetCompanyAsync(cik, historyStart, ct);
            if (sec?.BusinessAddress is not { } addr) { outcome.Excluded.Add($"CIK {cik}: no company record"); continue; }
            geo.LearnCityName(addr.Zip, addr.City, addr.State);

            if (!string.Equals(addr.State, o.Discovery.State, StringComparison.OrdinalIgnoreCase))
            { outcome.Excluded.Add($"{Label(sec)}: business address in {addr.State}"); continue; }
            if (geo.Locate(addr.Zip, addr.City) is not { } point)
            { outcome.Excluded.Add($"{Label(sec)}: unknown ZIP {addr.Zip} ({Text.TitleCase(addr.City)})"); continue; }
            if (geo.RegionAnchorFor(point) is null)
            { outcome.Excluded.Add($"{Label(sec)}: {Text.TitleCase(addr.City)} is outside the {o.Region.Name}"); continue; }
            if (sec.PrimaryTicker is null && !o.Listing.IncludeUnlisted)
            { outcome.Excluded.Add($"{Label(sec)}: not publicly traded (no ticker)"); continue; }

            candidates[cik] = new Candidate(sec, HeadquartersLocation(sec, addr, point), false, null);
        }

        // 2. Curated sites of companies headquartered elsewhere (or extra sites of Utah companies).
        var extraSites = new List<(long Cik, CompanyLocation Site)>();
        foreach (var row in ReadCurated())
        {
            var cik = await edgar.FindCikByTickerAsync(row.Ticker, ct);
            if (cik is null) { outcome.Warnings.Add($"Curated {row.Ticker}: ticker not found at the SEC."); continue; }
            if (geo.Locate(row.Zip, row.City) is not { } point) { outcome.Warnings.Add($"Curated {row.Ticker}: unknown ZIP {row.Zip}."); continue; }
            var site = new CompanyLocation
            {
                LocationId = $"{row.Ticker}-{row.Zip}", CompanyId = row.Ticker, Type = row.Type, Label = row.Label,
                Street = row.Street, City = row.City, State = "UT", PostalCode = row.Zip, Point = point
            };
            if (candidates.ContainsKey(cik.Value)) { extraSites.Add((cik.Value, site)); continue; }
            var sec = await edgar.GetCompanyAsync(cik.Value, historyStart, ct);
            if (sec is null) { outcome.Warnings.Add($"Curated {row.Ticker}: no SEC company record."); continue; }
            candidates[cik.Value] = new Candidate(sec, site, true, row.Note);
        }

        // 3. Financials, listing rules and executive pay per company.
        var parsedProxies = 0;
        foreach (var c in candidates.Values.OrderBy(c => c.Sec.Name))
        {
            ct.ThrowIfCancellationRequested();
            var ticker = c.Sec.PrimaryTicker ?? c.Sec.Cik10;
            var facts = await edgar.GetCompanyFactsAsync(c.Sec.Cik, ct);
            var fin = facts is null ? null : financials.Extract(ticker, c.Sec.Cik, facts, o.History.Years);
            if (fin is null || fin.Periods.Count == 0)
            { outcome.Excluded.Add($"{Label(c.Sec)}: no revenue data in XBRL filings"); continue; }

            var exchange = c.Sec.PrimaryExchange ?? "Unlisted";
            var listed = o.Listing.Exchanges.Contains(exchange, StringComparer.OrdinalIgnoreCase);
            if (!listed && (fin.LatestAnnualRevenue ?? 0) < o.Listing.MinOtcRevenue && !c.FromCurated)
            { outcome.Excluded.Add($"{Label(c.Sec)}: {exchange}, annual revenue {Money(fin.LatestAnnualRevenue)} below {Money(o.Listing.MinOtcRevenue)}"); continue; }

            // Executives: newest proxy first; older proxies only for years we don't have yet.
            var (pay, proxies, payWarnings) = await ReadExecutivePayAsync(c.Sec, ticker, historyStart.Year + 1, ct);
            parsedProxies += proxies;
            outcome.Warnings.AddRange(payWarnings.Select(w => $"{ticker}: {w}"));

            var company = new Company
            {
                CompanyId = ticker, Ticker = ticker, Name = Text.TitleCase(c.Sec.Name), Exchange = exchange,
                Sector = SectorClassifier.FromSic(c.Sec.Sic), Industry = Text.TitleCase(c.Sec.SicDescription),
                Website = string.IsNullOrWhiteSpace(c.Sec.Website) ? null : c.Sec.Website,
                FiscalYearEnd = c.Sec.FiscalYearEnd is { Length: 4 } fye ? $"{fye[..2]}-{fye[2..]}" : null,
                Description = c.Note, AsOfDate = fin.LatestPeriodEnd
            };
            outcome.Companies.Add(company);
            outcome.Locations.Add(c.Location with { CompanyId = ticker, LocationId = $"{ticker}-HQ" });
            outcome.Locations.AddRange(extraSites.Where(s => s.Cik == c.Sec.Cik).Select(s => s.Site with { CompanyId = ticker }));
            outcome.Financials.AddRange(fin.Periods.Select(p => p with { CompanyId = ticker }));
            foreach (var row in pay)
            {
                var personId = PersonId(row.Name);
                outcome.People.TryAdd(personId, new Person { PersonId = personId, Name = row.Name });
                outcome.Pay.Add(new ExecutiveCompensation
                {
                    CompanyId = ticker, PersonId = personId, ExecutiveName = row.Name, Title = row.Title, Year = row.Year,
                    Salary = row.Salary, Bonus = row.Bonus, StockAwards = row.StockAwards, Other = row.Other, Total = row.Total,
                    SourceFiling = row.Source
                });
            }
            outcome.Included.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {ticker} | {company.Name} | {c.Location.City} | {exchange} | {company.Sector} | {Money(fin.LatestAnnualRevenue)} | {pay.Select(p => p.Name).Distinct().Count()} | {(pay.Count > 0 ? $"{pay.Min(p => p.Year)}–{pay.Max(p => p.Year)}" : "—")} |"));
            logger.LogInformation("{Ticker,-6} {Name}: {Periods} periods, {People} executives from {Proxies} proxies",
                ticker, company.Name, fin.Periods.Count, pay.Select(p => p.Name).Distinct().Count(), proxies);
        }

        // 4. Write everything.
        var workbook = paths.Resolve(o.Output.WorkbookPath);
        ExcelWorkbookWriter.Write(workbook, outcome.Companies, outcome.Locations, outcome.Financials, outcome.Pay, outcome.People.Values,
            new Dictionary<string, string>
            {
                ["data_version"] = $"sec-{DateTime.UtcNow:yyyy.MM.dd}",
                ["as_of_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["is_sample"] = "false",
                ["source"] = "SEC EDGAR: company submissions, XBRL company facts, DEF 14A summary compensation tables. ZIP centroids: US Census Gazetteer.",
                ["region"] = o.Region.Name
            });
        var zips = await geo.WriteZipTableAsync(ct);
        await WriteReportAsync(outcome, zips, parsedProxies, ct);

        logger.LogInformation("Done: {Companies} companies, {Periods} financial periods, {Pay} pay rows for {People} people → {Path} ({Requests} network requests)",
            outcome.Companies.Count, outcome.Financials.Count, outcome.Pay.Count, outcome.People.Count, workbook, client.NetworkRequests);
        return 0;
    }

    private sealed record PayRow(string Name, string Title, int Year, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, string Source);

    private async Task<(List<PayRow> Rows, int Proxies, List<string> Warnings)> ReadExecutivePayAsync(SecCompany sec, string ticker, int fromYear, CancellationToken ct)
    {
        var rows = new Dictionary<(string Person, int Year), PayRow>();
        var warnings = new List<string>();
        var proxies = sec.Filings.Where(f => f.Form == "DEF 14A").OrderByDescending(f => f.FilingDate).ToList();
        var read = 0;
        var covered = new HashSet<int>();

        foreach (var proxy in proxies)
        {
            if (read >= options.Value.History.MaxProxiesPerCompany) break;
            // Each proxy reports the last fiscal year and the two before it.
            var expected = Enumerable.Range(proxy.FilingDate.Year - 3, 3).Where(y => y >= fromYear).ToList();
            if (expected.Count == 0) break;
            if (expected.All(covered.Contains)) continue;

            var html = await edgar.GetDocumentAsync(proxy.Url(sec.Cik), ct);
            read++;
            if (html is null) { warnings.Add($"proxy {proxy.FilingDate} could not be downloaded"); continue; }
            var result = compensation.Parse(html);
            if (result.Rows.Count == 0) { warnings.Add($"proxy {proxy.FilingDate}: no compensation table recognised"); continue; }
            warnings.AddRange(result.Warnings.Take(3).Select(w => $"proxy {proxy.FilingDate}: {w}"));

            foreach (var r in result.Rows.Where(r => r.Year >= fromYear))
            {
                covered.Add(r.Year);
                // Newer proxies win: they're read first, so only add what's missing.
                rows.TryAdd((PersonId(r.Name), r.Year),
                    new PayRow(r.Name, r.Title, r.Year, r.Salary, r.Bonus, r.StockAwards, r.Other, r.Total, proxy.Url(sec.Cik)));
            }
        }
        return (rows.Values.OrderBy(r => r.Name).ThenBy(r => r.Year).ToList(), read, warnings);
    }

    /// <summary>
    /// People are linked across companies by normalised name ("Steven R. Fife" → "steven-fife").
    /// Good enough within one region; a later pass can switch to SEC CIKs from Forms 3/4.
    /// </summary>
    internal static string PersonId(string name)
    {
        var n = name.ToLowerInvariant().Replace("’", "'");
        n = Regex.Replace(n, @"\b(jr|sr|ii|iii|iv|md|phd|cpa)\b\.?", " ");
        var words = Regex.Replace(n, @"[^a-z\s-]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        return words.Count >= 2 ? $"{words[0]}-{words[^1]}" : string.Join('-', words);
    }

    private static CompanyLocation HeadquartersLocation(SecCompany sec, SecAddress addr, GeoPoint point) => new()
    {
        LocationId = "HQ", CompanyId = sec.PrimaryTicker ?? sec.Cik10, Type = LocationType.Headquarters, Label = "Headquarters",
        Street = Text.TitleCase(addr.Street), City = Text.TitleCase(addr.City), State = addr.State.ToUpperInvariant(),
        PostalCode = addr.Zip.Length >= 5 ? addr.Zip[..5] : addr.Zip, Point = point
    };

    private sealed record CuratedRow(string Ticker, LocationType Type, string Label, string Street, string City, string Zip, string? Note);

    private IEnumerable<CuratedRow> ReadCurated()
    {
        var path = paths.Resolve(options.Value.CuratedOfficesPath);
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var f = line.Split(',');
            if (f.Length < 6 || !Enum.TryParse<LocationType>(f[1], true, out var type)) continue;
            yield return new CuratedRow(f[0].Trim().ToUpperInvariant(), type, f[2].Trim(), f[3].Trim(), f[4].Trim(), f[5].Trim(),
                f.Length > 6 && f[6].Trim().Length > 0 ? f[6].Trim() : null);
        }
    }

    private async Task WriteReportAsync(Outcome o, int zipCount, int proxies, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine($"Region: **{options.Value.Region.Name}**. Source: SEC EDGAR (submissions, XBRL company facts, DEF 14A). ZIP centroids: US Census Gazetteer.").AppendLine();
        sb.AppendLine($"- Companies included: **{o.Companies.Count}**");
        sb.AppendLine($"- Financial periods: {o.Financials.Count}");
        sb.AppendLine($"- Executive pay rows: {o.Pay.Count} for {o.People.Count} people, from {proxies} proxy statements");
        sb.AppendLine($"- ZIP table rows written: {zipCount}");
        sb.AppendLine($"- Network requests this run: {client.NetworkRequests} (the rest came from the local cache)").AppendLine();
        sb.AppendLine("## Included").AppendLine();
        sb.AppendLine("| Ticker | Company | City | Exchange | Sector | Latest annual revenue | Executives | Pay years |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var line in o.Included.OrderBy(x => x)) sb.AppendLine(line);
        sb.AppendLine().AppendLine("## Excluded").AppendLine();
        foreach (var line in o.Excluded.OrderBy(x => x)) sb.AppendLine($"- {line}");
        sb.AppendLine().AppendLine("## Needs review").AppendLine();
        sb.AppendLine("Rows where the pay components didn't add up to the total, or a table couldn't be read, are listed here. Check them against the linked filing.").AppendLine();
        foreach (var line in o.Warnings) sb.AppendLine($"- {line}");
        await File.WriteAllTextAsync(paths.Resolve(options.Value.Output.ReportPath), sb.ToString(), ct);
    }

    private static string Label(SecCompany s) => $"{s.PrimaryTicker ?? $"CIK {s.Cik}"} {Text.TitleCase(s.Name)}";

    private static string Money(decimal? v) => v switch
    {
        null => "—",
        >= 1_000_000_000 => $"${v / 1_000_000_000:0.00}B",
        >= 1_000_000 => $"${v / 1_000_000:0.0}M",
        _ => $"${v / 1_000:0}K"
    };
}
