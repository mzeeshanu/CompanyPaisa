using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Uk;

/// <summary>
/// Builds the UK workbook (FTSE 350, excluding investment trusts):
/// constituents → LEI via filings.xbrl.org names → HQ address (GLEIF) → postcode district →
/// ~6 years of IFRS figures from ESEF xBRL-JSON → directors' single total figure from the latest annual reports.
/// </summary>
public sealed partial class UkImportPipeline(IOptions<ImporterOptions> options, RepoPaths paths, ILoggerFactory loggers)
{
    private readonly UkOptions _o = options.Value.Uk;
    private readonly ILogger _log = loggers.CreateLogger<UkImportPipeline>();
    private readonly HaversineDistanceCalculator _distance = new();

    public async Task<int> RunAsync(bool refreshList, CancellationToken ct)
    {
        using var client = new SecClient(new SecOptions
        {
            UserAgent = _o.UserAgent, MaxRequestsPerSecond = _o.MaxRequestsPerSecond, CacheDirectory = _o.CacheDirectory,
            IndexCacheHours = _o.IndexCacheHours, CompressCache = true
        }, paths, loggers.CreateLogger<SecClient>());

        // 1. Constituents (reviewable CSV; rebuilt from Wikipedia on request or when missing).
        var listPath = paths.Resolve(_o.ConstituentsPath);
        var constituents = UkConstituents.Read(listPath);
        if (refreshList || constituents.Count == 0)
        {
            var fresh = await UkConstituents.FetchAsync(client, _o.ConstituentPages, ct);
            var keepLei = constituents.Where(c => c.Lei is not null).ToDictionary(c => c.Ticker, c => c.Lei);
            constituents = fresh.Select(c => c with { Lei = keepLei.GetValueOrDefault(c.Ticker) }).ToList();
            _log.LogInformation("Constituent list: {Count} companies from {Pages}", constituents.Count, string.Join(", ", _o.ConstituentPages));
        }

        var postcodes = await UkPostcodes.LoadAsync(client, _o.PostcodeDistrictsUrl, ct);
        var entities = await UkFilingsIndex.LoadAsync(client, _o.FilingsApi, ct);
        _log.LogInformation("{Entities} UK filers with ESEF reports, {Districts} postcode districts", entities.Count, postcodes.Count);
        var byLei = entities.ToDictionary(e => e.Lei, StringComparer.OrdinalIgnoreCase);
        var byName = entities.GroupBy(e => NormaliseName(e.Name)).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Filings.Count).First());

        // 1b. The rest of the Main Market: filers no FTSE constituent claimed, with London-listed ordinary shares.
        var added = new List<UkConstituent>();
        if (_o.AllMainMarket)
        {
            var claimed = constituents
                .Select(c => c.Lei is not null ? byLei.GetValueOrDefault(c.Lei) : byName.GetValueOrDefault(NormaliseName(c.Name)) ?? FuzzyMatch(c.Name, entities))
                .Where(e => e is not null).Select(e => e!.Lei).ToHashSet(StringComparer.OrdinalIgnoreCase);
            using var mainMarket = new UkMainMarket(client, _o.GleifApi, paths.Resolve(_o.CacheDirectory), _log);
            added = await mainMarket.FindAsync(entities.Where(e => !claimed.Contains(e.Lei)),
                constituents.Select(c => c.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase), recentYears: 2, ct);
            UkConstituents.Write(paths.Resolve(_o.MainMarketListPath), added.OrderBy(a => a.Name));
            constituents = [.. constituents, .. added];
        }
        var addedTickers = added.Select(a => a.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var companies = new List<Company>();
        var locations = new List<CompanyLocation>();
        var periods = new List<FinancialPeriod>();
        var pay = new List<ExecutiveCompensation>();
        var people = new Dictionary<string, Person>();
        var included = new List<(string Region, string Line)>();
        var excluded = new List<string>();
        var warnings = new List<string>();
        var matched = new List<UkConstituent>();
        int reportsRead = 0, payRowsVerified = 0;

        foreach (var c in constituents.OrderBy(c => c.Name))
        {
            ct.ThrowIfCancellationRequested();
            if (_o.ExcludedSectors.Any(s => c.Sector.Contains(s, StringComparison.OrdinalIgnoreCase)))
            { excluded.Add($"{c.Ticker} {c.Name}: {c.Sector} (investment trust / fund)"); matched.Add(c); continue; }

            // 2. Which filer is it? A hand-set LEI wins, else the normalised name.
            var entity = c.Lei is not null ? byLei.GetValueOrDefault(c.Lei) : byName.GetValueOrDefault(NormaliseName(c.Name)) ?? FuzzyMatch(c.Name, entities);
            matched.Add(c with { Lei = c.Lei ?? entity?.Lei });
            if (entity is null) { excluded.Add($"{c.Ticker} {c.Name}: no ESEF annual reports found on filings.xbrl.org (set its LEI in {_o.ConstituentsPath} if it's a naming mismatch)"); continue; }

            // 3. Where is it? GLEIF's headquarters address; UK only.
            var (hq, legal) = await Gleif.GetAddressesAsync(client, _o.GleifApi, entity.Lei, ct);
            var address = hq is { Country: "GB" } ? hq : hq is null && legal is { Country: "GB" } ? legal : null;
            if (address is null) { excluded.Add($"{c.Ticker} {c.Name}: headquartered outside the UK ({hq?.City}, {hq?.Country})"); continue; }
            if (postcodes.Locate(address.PostalCode) is not { } place) { excluded.Add($"{c.Ticker} {c.Name}: unknown postcode {address.PostalCode}"); continue; }
            var region = RegionFor(place.Point);

            // 4. Financials: every report gives its year and the comparative; newer reports win (restatements).
            var years = await EsefFinancials.CollectYearsAsync(client, entity, c.Ticker, warnings, ct);
            if (years.Count == 0) { excluded.Add($"{c.Ticker} {c.Name}: no revenue in its tagged reports"); continue; }
            var latest = years.Values.MaxBy(y => y.Year.FiscalYear).Year;
            var currency = latest.Currency;
            var companyId = $"{c.Ticker}.L";

            // 5. Directors' pay from the latest annual reports.
            var reports = entity.Filings.OrderByDescending(f => f.PeriodEnd).Take(_o.PayReports).ToList();
            var found = new List<(UkPayRow Row, string Source)>();
            string? payCurrency = null;   // many groups report in USD but pay their directors in GBP
            foreach (var report in reports)
            {
                var result = await ReadPayAsync(client, report.ReportUrl, ct);
                reportsRead++;
                warnings.AddRange(result.Warnings.Select(w => $"{c.Ticker}: report {report.PeriodEnd}: {w}"));
                // Same checks the parser now makes, applied to results cached by earlier runs, plus "that's the company, not a person".
                // A report covers its own year and the one before; any other year is a misread cell (National Grid "2031").
                var rows = result.Rows.Select(r => r with { Name = RemunerationParser.CleanName(r.Name) })
                    .Where(r => RemunerationParser.LooksLikeName(r.Name) && RemunerationParser.TotalIsLargest(r) && !IsCompanyName(r.Name, c.Name)
                                && r.Year >= report.PeriodEnd.Year - 2 && r.Year <= report.PeriodEnd.Year + 1).ToList();
                var implausible = rows.Where(r => !RemunerationParser.PlausibleExecutivePay(r)).ToList();
                foreach (var r in implausible.Where(r => r.Total >= 150_000m))
                    warnings.Add($"{c.Ticker}: report {report.PeriodEnd}: {r.Name} {r.Year} read as total {r.Total:N0}, salary {r.Salary:N0} — not reliable enough to publish; left out");
                rows = rows.Except(implausible).ToList();
                if (rows.Count < result.Rows.Count) warnings.Add($"{c.Ticker}: report {report.PeriodEnd}: ignored {result.Rows.Count - rows.Count} row(s) that weren't an executive director's pay");
                if (rows.Count == 0) continue;
                payCurrency ??= result.Currency;   // the newest report with a table decides
                if (result.Currency != payCurrency)
                { warnings.Add($"{c.Ticker}: report {report.PeriodEnd}: pay in {result.Currency}, newer reports in {payCurrency}; skipped"); continue; }
                found.AddRange(rows.Select(r => (r, report.ReportUrl)));
            }
            // One person, one name: "D. Seekings" and "Ms Halai" join the full name used elsewhere in the same company's reports.
            var canonical = CanonicalNames(found.Select(f => f.Row.Name));
            var payRows = new Dictionary<(string, int), (UkPayRow Row, string Source)>();
            foreach (var (row, source) in found)
            {
                var r = row with { Name = canonical[row.Name] };
                payRows.TryAdd((PersonSlug(r.Name), r.Year), (r, source));   // newest report first, so restated years win
            }

            companies.Add(new Company
            {
                CompanyId = companyId, Ticker = companyId, Name = c.Name, Exchange = "LSE", Sector = SectorClassifier.FromIcb(c.Sector),
                Industry = c.Sector, Currency = currency, PayCurrency = payCurrency is not null && payCurrency != currency ? payCurrency : null, FiscalYearEnd = latest.PeriodEnd.ToString("MM-dd", CultureInfo.InvariantCulture),
                AsOfDate = latest.PeriodEnd
            });
            locations.Add(new CompanyLocation
            {
                LocationId = $"{companyId}-HQ", CompanyId = companyId, Type = LocationType.Headquarters, Label = "Headquarters",
                Street = TitleCase(address.Street), City = TitleCase(address.City), State = "UK", PostalCode = address.PostalCode.ToUpperInvariant(), Point = place.Point
            });
            periods.AddRange(years.Values.Select(y => EsefFinancials.ToPeriod(companyId, y.Year, y.Source)));
            foreach (var ((slug, _), (r, source)) in payRows)
            {
                var personId = $"{slug}-{c.Ticker.ToLowerInvariant()}-l";
                people.TryAdd(personId, new Person { PersonId = personId, Name = r.Name });
                if (r.Verified) payRowsVerified++;
                else warnings.Add($"{c.Ticker}: {r.Name} {r.Year}: components don't add up to the stated total {r.Total:N0}; difference shown as Other");
                pay.Add(new ExecutiveCompensation
                {
                    CompanyId = companyId, PersonId = personId, ExecutiveName = r.Name, Title = "Executive Director", Year = r.Year,
                    Salary = r.Salary, Bonus = r.Bonus, StockAwards = r.LongTerm, Other = r.Other, Total = r.Total, SourceFiling = source
                });
            }

            var directors = payRows.Values.Select(p => p.Row.Name).Distinct().Count();
            included.Add((region, string.Create(CultureInfo.InvariantCulture,
                $"| {companyId} | {c.Name} | {TitleCase(address.City)} | {companies[^1].Sector} | {Money(latest.Revenue, currency)} | {years.Keys.Min()}–{years.Keys.Max()} | {directors} | {(payRows.Count > 0 ? $"{payRows.Keys.Min(k => k.Item2)}–{payRows.Keys.Max(k => k.Item2)}" : "—")} |")));
            _log.LogInformation("{Ticker,-7} {Name}: {Years} years, {Directors} directors, {Region}", companyId, c.Name, years.Count, directors, region);
        }

        // 6. Keep the reviewed list (with the LEIs we matched) for next time.
        UkConstituents.Write(listPath, matched.Where(m => !addedTickers.Contains(m.Ticker)));

        var workbook = paths.Resolve(_o.WorkbookPath);
        var staging = workbook + ".new.xlsx";
        ExcelWorkbookWriter.Write(staging, companies, locations, periods, pay, people.Values, new Dictionary<string, string>
        {
            ["data_version"] = $"uk-{DateTime.UtcNow:yyyy.MM.dd}",
            ["as_of_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["is_sample"] = "false",
            ["source"] = "ESEF annual reports via filings.xbrl.org; headquarters from GLEIF; postcode districts from GeoNames.",
            ["region"] = "United Kingdom"
        });
        var (vc, vl) = ExcelWorkbookWriter.Verify(staging);
        File.Move(staging, workbook, overwrite: true);
        var districts = await postcodes.WriteTableAsync(paths.Resolve(_o.PostcodeTableOutput), ct);
        await WriteReportAsync(companies.Count, periods.Count, pay.Count, payRowsVerified, people.Count, reportsRead, districts, included, excluded, warnings, client.NetworkRequests, ct);
        _log.LogInformation("Done: {Companies} UK companies ({Verified} verified in the API loader), {Periods} years of figures, {Pay} pay rows for {People} directors → {Path}",
            companies.Count, vc, periods.Count, pay.Count, people.Count, workbook);
        return 0;
    }

    /// <summary>Bump when the pay parser learns a new layout; reports it previously couldn't read are then re-read.</summary>
    private const string ParserVersion = "v2";
    private static readonly string[] OlderParserVersions = ["v1"];

    /// <summary>Annual reports are 5–40 MB, so they aren't cached — only what we parsed from them is.</summary>
    private async Task<UkPayResult> ReadPayAsync(ISecClient client, string url, CancellationToken ct)
    {
        var dir = paths.Resolve(Path.Combine(_o.CacheDirectory, "pay"));
        Directory.CreateDirectory(dir);
        string FileFor(string version) => Path.Combine(dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url + "|" + version)))[..32] + ".json");
        var file = FileFor(ParserVersion);
        if (File.Exists(file)) return JsonSerializer.Deserialize<UkPayResult>(await File.ReadAllTextAsync(file, ct))!;
        // A result from an older parser is kept if it found a table; reports it couldn't read are read again.
        foreach (var older in OlderParserVersions)
            if (File.Exists(FileFor(older)) && JsonSerializer.Deserialize<UkPayResult>(await File.ReadAllTextAsync(FileFor(older), ct)) is { Rows.Count: > 0 } kept)
                return kept;
        var html = await client.GetStringAsync(url, CachePolicy.NoStore, ct);
        var result = html is null ? new UkPayResult([], ["annual report could not be downloaded"], "GBP") : RemunerationParser.Parse(html);
        if (html is not null) await File.WriteAllTextAsync(file, JsonSerializer.Serialize(result), ct);
        return result;
    }

    private string RegionFor(GeoPoint point)
    {
        foreach (var r in _o.Regions)
            if (r.Anchors.Any(a => _distance.DistanceMiles(point, new GeoPoint(a.Latitude, a.Longitude)) <= a.RadiusMiles)) return r.Name;
        return _o.Regions.FirstOrDefault(r => r.States.Contains("UK", StringComparer.OrdinalIgnoreCase))?.Name ?? "United Kingdom";
    }

    /// <summary>"Rolls-Royce Holdings" and "ROLLS-ROYCE HOLDINGS PLC" → "ROLLSROYCE".</summary>
    internal static string NormaliseName(string name)
    {
        var n = name.ToUpperInvariant().Replace("&", " AND ").Replace("’", "'");
        n = NameNoise().Replace(n, " ");
        return Regex.Replace(n, "[^A-Z0-9]", "");
    }

    /// <summary>
    /// When the normalised names differ ("Sainsbury's" / "J SAINSBURY PLC", "JD Sports" / "JD SPORTS FASHION PLC"):
    /// every word of the index name must start a word of the legal name, and exactly one filer may fit.
    /// </summary>
    internal static UkEntity? FuzzyMatch(string indexName, IReadOnlyList<UkEntity> entities)
    {
        static string[] Words(string s) => Regex.Replace(NameNoise().Replace(s.ToUpperInvariant().Replace("&", " AND ").Replace("'S", "").Replace("’S", ""), " "), "[^A-Z0-9 ]", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wanted = Words(indexName);
        if (wanted.Length == 0) return null;
        var fits = entities.Where(e =>
        {
            var legal = Words(e.Name);
            return legal.Length > 0 && legal.Length <= wanted.Length + 2 && wanted.All(w => legal.Any(l => l.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(l, StringComparison.Ordinal) && l.Length >= 4));
        }).ToList();
        // Several filers can share a name (a group and its bank); the one with most annual reports is the listed parent.
        return fits.Count switch { 0 => null, 1 => fits[0], _ => fits.GroupBy(f => Words(f.Name).Length).OrderBy(g => g.Key).First().MaxBy(f => f.Filings.Count) };
    }

    /// <summary>"BAE Systems" in BAE Systems' own pay table is a heading, not a director.</summary>
    internal static bool IsCompanyName(string candidate, string companyName)
    {
        var n = NormaliseName(candidate);
        var co = NormaliseName(companyName);
        return n.Length > 0 && (co.Contains(n, StringComparison.Ordinal) || n.Contains(co, StringComparison.Ordinal));
    }

    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase) { "Mr", "Mrs", "Ms", "Miss", "Dr", "Sir", "Dame", "Lord", "Baroness" };

    /// <summary>
    /// Maps each printed name to the fullest version of it at the same company: same surname, and the same first initial
    /// (or no first name at all, as in "Ms Halai") — but only when exactly one full name fits, so two Smiths stay apart.
    /// </summary>
    internal static Dictionary<string, string> CanonicalNames(IEnumerable<string> names)
    {
        var distinct = names.Distinct().ToList();
        var result = distinct.ToDictionary(n => n, n => n);
        static string[] Words(string n) => n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Honorifics.Contains(w.Trim('.'))).ToArray();
        static bool IsFull(string[] w) => w.Length >= 2 && w[0].Trim('.').Length > 1;
        foreach (var group in distinct.GroupBy(n => Words(n) is { Length: > 0 } w ? w[^1].Trim('.', ',').ToUpperInvariant() : n.ToUpperInvariant()))
        {
            var full = group.Where(n => IsFull(Words(n))).ToList();
            foreach (var name in group.Where(n => !IsFull(Words(n)) || full.Count > 0))
            {
                var w = Words(name);
                char? initial = w.Length >= 2 ? char.ToUpperInvariant(w[0][0]) : null;
                var fits = full.Where(f => f != name && (initial is null || char.ToUpperInvariant(Words(f)[0][0]) == initial)).Distinct().ToList();
                if (!IsFull(w) && fits.Count == 1) result[name] = fits[0];
            }
        }
        return result;
    }

    private static string PersonSlug(string name)
    {
        var n = Regex.Replace(name.ToLowerInvariant(), @"\b(dame|sir|dr|lord|baroness|mr|mrs|ms)\b\.?", " ");
        var words = Regex.Replace(n, @"[^a-z\s-]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        return words.Count >= 2 ? $"{words[0]}-{words[^1]}" : string.Join('-', words);
    }

    private static string TitleCase(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower) ? s.Trim() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    private static string Money(decimal v, string currency)
    {
        var sym = currency switch { "GBP" => "£", "USD" => "$", "EUR" => "€", _ => currency + " " };
        return v >= 1_000_000_000 ? $"{sym}{v / 1_000_000_000:0.00}B" : v >= 1_000_000 ? $"{sym}{v / 1_000_000:0.0}M" : $"{sym}{v / 1_000:0}K";
    }

    private async Task WriteReportAsync(int companies, int periods, int payRows, int verified, int people, int reports, int districts,
        List<(string Region, string Line)> included, List<string> excluded, List<string> warnings, int requests, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# UK import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine(_o.AllMainMarket
            ? "Scope: UK companies on the London Stock Exchange Main Market that file ESEF annual reports (FTSE 350 plus the rest; tickers via GLEIF ISINs and OpenFIGI), minus investment trusts and funds. Sources: ESEF annual reports (filings.xbrl.org), GLEIF headquarters addresses, GeoNames postcode districts."
            : "Scope: FTSE 350 (FTSE 100 + FTSE 250) minus investment trusts. Sources: ESEF annual reports (filings.xbrl.org), GLEIF headquarters addresses, GeoNames postcode districts.").AppendLine();
        sb.AppendLine($"- Companies included: **{companies}**");
        sb.AppendLine($"- Years of figures: {periods}");
        sb.AppendLine($"- Directors' pay rows: {payRows} for {people} directors ({verified} add up exactly), from {reports} annual reports");
        sb.AppendLine($"- Postcode districts written: {districts}");
        sb.AppendLine($"- Network requests this run: {requests}").AppendLine();
        sb.AppendLine("| Area | Companies |").AppendLine("|---|---|");
        foreach (var g in included.GroupBy(i => i.Region).OrderByDescending(g => g.Count())) sb.AppendLine($"| {g.Key} | {g.Count()} |");
        foreach (var g in included.GroupBy(i => i.Region).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine().AppendLine($"## Included — {g.Key} ({g.Count()})").AppendLine();
            sb.AppendLine("| Ticker | Company | City | Sector | Latest revenue | Years | Directors | Pay years |").AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var line in g.Select(i => i.Line).Order()) sb.AppendLine(line);
        }
        sb.AppendLine().AppendLine($"## Excluded ({excluded.Count})").AppendLine();
        foreach (var line in excluded.Order()) sb.AppendLine($"- {line}");
        sb.AppendLine().AppendLine("## Needs review").AppendLine();
        foreach (var line in warnings) sb.AppendLine($"- {line}");
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }

    [GeneratedRegex(@"\b(PUBLIC LIMITED COMPANY|PLC|P\.L\.C\.?|LIMITED|LTD|GROUP|HOLDINGS?|THE|COMPANY|CO|INTERNATIONAL|SE|NV|SA|AG|INC|CORPORATION|CORP)\b\.?")]
    private static partial Regex NameNoise();
}
