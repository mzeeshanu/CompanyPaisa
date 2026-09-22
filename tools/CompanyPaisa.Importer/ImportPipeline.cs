using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Compensation;
using CompanyPaisa.Importer.Financials;
using CompanyPaisa.Importer.Geo;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer;

/// <summary>
/// Builds the SEC market of the CompanyPaisa database ("sec": US, Canada, Australia, New Zealand):
/// every listed filer → keep reporting companies with a covered address (+ curated offices) → XBRL financials →
/// proxy-statement executive pay (checked against the tagged CEO totals) → publish, ZIP table and a review report.
/// </summary>
public sealed partial class ImportPipeline(
    IEdgarService edgar,
    IFinancialsExtractor financials,
    ICompensationParser compensation,
    IZipGeocoder geo,
    ISecClient client,
    Publishing.DataPublisher publisher,
    Compensation.NewHireReader newHires,
    IOptions<ImporterOptions> options,
    RepoPaths paths,
    ILogger<ImportPipeline> logger) : Publishing.IMarketImporter
{
    string Publishing.IMarketImporter.Market => Market;
    public string Description => "SEC filers with a US, Canadian, Australian or New Zealand address: XBRL financials and proxy-statement pay";
    Task<int> Publishing.IMarketImporter.RunAsync(bool refreshLists, CancellationToken ct) => RunAsync(ct);

    private sealed record Candidate(SecCompany Sec, CompanyLocation Location, string Region, bool FromCurated, string? Note);

    private sealed class Outcome
    {
        public List<Company> Companies { get; } = [];
        public List<CompanyLocation> Locations { get; } = [];
        public List<FinancialPeriod> Financials { get; } = [];
        public List<ExecutiveCompensation> Pay { get; } = [];
        public List<NewExecutive> NewExecutives { get; } = [];
        public List<WorkerPay> WorkerPay { get; } = [];
        public Dictionary<string, Person> People { get; } = new();
        public List<(string Region, string Line)> Included { get; } = [];
        public int NoTicker { get; set; }
        public int OutsideMetros { get; set; }
        public int NotReporting { get; set; }
        public int Abroad { get; set; }
        public int PeopleLinkedByCik { get; set; }
        public int PeopleByNameOnly { get; set; }
        /// <summary>CEO pay rows the proxy's pay-versus-performance tags confirm, correct or reject; tagged CEO-years with no row.</summary>
        public int PayVerified { get; set; }
        public int PayCorrected { get; set; }
        public int PayDropped { get; set; }
        public int PayUnmatched { get; set; }
        public List<string> Excluded { get; } = [];
        public List<string> Warnings { get; } = [];
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        var historyStart = new DateOnly(DateTime.UtcNow.Year - o.History.Years - 1, 1, 1);
        var outcome = new Outcome();
        await geo.LoadAsync(ct);

        // 1. Filers in the configured states (EDGAR full-text search), filtered to the covered metros and to listed companies.
        var candidates = new Dictionary<long, Candidate>();
        var discovered = await edgar.DiscoverFilerCiksAsync(ct);
        var seen = 0;
        foreach (var cik in discovered)
        {
            if (++seen % 250 == 0) logger.LogInformation("Screening filers: {Seen}/{Total} ({Kept} in covered metros so far)", seen, discovered.Count, candidates.Count);
            var sec = await edgar.GetCompanyAsync(cik, historyStart, ct);
            if (sec?.BusinessAddress is not { } addr) { outcome.Excluded.Add($"CIK {cik}: no company record"); continue; }
            geo.LearnCityName(addr.Zip, addr.City, addr.State);

            // Cheapest test first: most filers are funds, trusts and shells with no ticker.
            // Thousands of filers fall out here, so they're counted rather than listed one by one in the report.
            if (sec.PrimaryTicker is null && !o.Listing.IncludeUnlisted) { outcome.NoTicker++; continue; }
            if (sec.PrimaryTicker is { } t && o.Listing.SkipTickers.TryGetValue(t, out var why)) { outcome.Excluded.Add($"{Label(sec)}: {why}"); continue; }
            // Still reporting? The ticker file also lists companies that stopped filing years ago.
            if (!sec.Filings.Any(f => StillReportingForms.Contains(f.Form) && f.FilingDate >= o.Discovery.Since)) { outcome.NotReporting++; continue; }
            if (geo.Place(addr) is not { } placed)
            {
                // Listed in the US but based abroad (ADRs, Israeli and Chinese companies…): counted, not listed.
                if (addr.State is { Length: 2 } s && char.IsLetter(s[0]) && char.IsLetter(s[1]) || addr.State is "A0" or "A1" or "A2" or "A3" or "A4" or "A5" or "A6" or "A7" or "A8" or "A9" or "B0" or "Z4")
                    outcome.Excluded.Add($"{Label(sec)}: unknown postal code {addr.Zip} ({Text.TitleCase(addr.City)}, {addr.State})");
                else outcome.Abroad++;
                continue;
            }
            if (geo.RegionFor(placed.Point, placed.State, placed.Country) is not { } region) { outcome.OutsideMetros++; continue; }
            candidates[cik] = new Candidate(sec, HeadquartersLocation(sec, addr, placed), region.Region, false, null);
        }

        // 2. Curated sites of companies headquartered elsewhere (or extra sites of Utah companies).
        var extraSites = new List<(long Cik, CompanyLocation Site)>();
        foreach (var row in ReadCurated())
        {
            var cik = await edgar.FindCikByTickerAsync(row.Ticker, ct);
            if (cik is null) { outcome.Warnings.Add($"Curated {row.Ticker}: ticker not found at the SEC."); continue; }
            var state = geo.StateOf(row.Zip);
            if (state is null || geo.Locate(row.Zip, row.City, state) is not { } point) { outcome.Warnings.Add($"Curated {row.Ticker}: unknown ZIP {row.Zip}."); continue; }
            var site = new CompanyLocation
            {
                LocationId = $"{row.Ticker}-{row.Zip}", CompanyId = row.Ticker, Type = row.Type, Label = row.Label,
                Street = row.Street, City = row.City, State = state, PostalCode = row.Zip, Point = point
            };
            if (candidates.ContainsKey(cik.Value)) { extraSites.Add((cik.Value, site)); continue; }
            var sec = await edgar.GetCompanyAsync(cik.Value, historyStart, ct);
            if (sec is null) { outcome.Warnings.Add($"Curated {row.Ticker}: no SEC company record."); continue; }
            candidates[cik.Value] = new Candidate(sec, site, geo.RegionFor(point, state)?.Region ?? "Other", true, row.Note);
        }

        // A ticker can be claimed by two SEC entities (a bank and its holding company, say). Keep the one the SEC's
        // ticker file assigns it to, so company ids stay unique.
        foreach (var dup in candidates.Values.Where(c => c.Sec.PrimaryTicker is not null).GroupBy(c => c.Sec.PrimaryTicker!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
        {
            var owner = await edgar.FindCikByTickerAsync(dup.Key, ct);
            var keep = dup.FirstOrDefault(c => c.Sec.Cik == owner) ?? dup.OrderByDescending(c => c.Sec.Filings.Count).First();
            foreach (var drop in dup.Where(c => c != keep))
            {
                candidates.Remove(drop.Sec.Cik);
                outcome.Excluded.Add($"{Label(drop.Sec)} (CIK {drop.Sec.Cik}): ticker {dup.Key} belongs to CIK {keep.Sec.Cik}");
            }
        }

        // 3. Financials, listing rules and executive pay per company.
        var parsedProxies = 0;
        var done = 0;
        foreach (var c in candidates.Values.OrderBy(c => c.Sec.Name))
        {
            ct.ThrowIfCancellationRequested();
            if (++done % 100 == 0) logger.LogInformation("Companies: {Done}/{Total}", done, candidates.Count);
            var ticker = c.Sec.PrimaryTicker ?? c.Sec.Cik10;
            var facts = await edgar.GetCompanyFactsAsync(c.Sec.Cik, ct);
            var fin = facts is null ? null : financials.Extract(ticker, c.Sec.Cik, facts, o.History.Years);
            if (fin is null || fin.Periods.Count == 0)
            { outcome.Excluded.Add($"{Label(c.Sec)}: no revenue data in XBRL filings"); continue; }
            // Figures more than two years old describe a company that's since been taken over, gone private or stopped
            // tagging its reports; showing them as "current" would mislead.
            var newest = fin.Periods.Max(p => p.FiscalYear);
            if (newest < DateTime.UtcNow.Year - 2 && !c.FromCurated)
            { outcome.Excluded.Add($"{Label(c.Sec)}: newest figures are for {newest}"); continue; }

            var exchange = c.Sec.PrimaryExchange ?? "Unlisted";
            var listed = o.Listing.Exchanges.Contains(exchange, StringComparer.OrdinalIgnoreCase);
            if (!listed && (fin.LatestAnnualRevenue ?? 0) < o.Listing.MinOtcRevenue && !c.FromCurated)
            { outcome.Excluded.Add($"{Label(c.Sec)}: {exchange}, annual revenue {Money(fin.LatestAnnualRevenue)} below {Money(o.Listing.MinOtcRevenue)}"); continue; }

            // Executives: newest proxy first; older proxies only for years we don't have yet.
            var payResult = await ReadExecutivePayAsync(c.Sec, ticker, historyStart.Year + 1, ct);
            var pay = payResult.Rows;
            var proxies = payResult.Proxies;
            parsedProxies += proxies;
            outcome.Warnings.AddRange(payResult.Warnings.Select(w => $"{ticker}: {w}"));
            outcome.PayVerified += payResult.Verified;
            outcome.PayCorrected += payResult.Corrected;
            outcome.PayDropped += payResult.Dropped;
            outcome.PayUnmatched += payResult.Unmatched;
            outcome.WorkerPay.AddRange(payResult.Ratios.Select(r => new WorkerPay
            {
                CompanyId = ticker, Year = r.Year, MedianEmployeePay = r.Ratio.MedianEmployeePay, CeoPay = r.Ratio.CeoPay, Ratio = r.Ratio.Ratio, SourceFiling = r.Source
            }));

            var company = new Company
            {
                CompanyId = ticker, Ticker = ticker, Name = Text.TitleCase(c.Sec.Name), Exchange = exchange,
                Sector = SectorClassifier.FromSic(c.Sec.Sic), Industry = Text.TitleCase(c.Sec.SicDescription),
                Website = string.IsNullOrWhiteSpace(c.Sec.Website) ? null : c.Sec.Website,
                FiscalYearEnd = c.Sec.FiscalYearEnd is { Length: 4 } fye ? $"{fye[..2]}-{fye[2..]}" : null,
                Description = c.Note, AsOfDate = fin.LatestPeriodEnd,
                // Canadian companies often report in Canadian dollars; US proxy statements show pay in US dollars.
                Currency = fin.Currency, PayCurrency = pay.Count > 0 && fin.Currency != "USD" ? "USD" : null
            };
            outcome.Companies.Add(company);
            outcome.Locations.Add(c.Location with { CompanyId = ticker, LocationId = $"{ticker}-HQ" });
            outcome.Locations.AddRange(extraSites.Where(s => s.Cik == c.Sec.Cik).Select(s => s.Site with { CompanyId = ticker }));
            outcome.Financials.AddRange(fin.Periods.Select(p => p with { CompanyId = ticker }));

            // Give each executive a nationwide id: their SEC insider CIK when we can match the name, else company-scoped.
            var ids = new Dictionary<string, string>();
            if (pay.Count > 0)
            {
                var insiders = await edgar.GetInsidersAsync(c.Sec.Cik, ct);
                foreach (var name in pay.Select(p => p.Name).DistinctBy(PersonId))
                {
                    var match = InsiderMatcher.Match(name, insiders);
                    ids[PersonId(name)] = match is null ? $"{PersonId(name)}-{ticker.ToLowerInvariant()}" : $"{PersonId(name)}-{match.Cik}";
                    if (match is null) outcome.PeopleByNameOnly++; else outcome.PeopleLinkedByCik++;
                }
            }
            foreach (var row in pay)
            {
                var personId = ids[PersonId(row.Name)];
                outcome.People.TryAdd(personId, new Person { PersonId = personId, Name = row.Name });
                outcome.Pay.Add(new ExecutiveCompensation
                {
                    CompanyId = ticker, PersonId = personId, ExecutiveName = row.Name, Title = row.Title, Year = row.Year,
                    Salary = row.Salary, Bonus = row.Bonus, StockAwards = row.StockAwards, Other = row.Other, Total = row.Total,
                    SourceFiling = row.Source
                });
            }
            // Officers appointed recently, with the package the company announced (8-K Item 5.02).
            outcome.NewExecutives.AddRange(await newHires.ReadAsync(c.Sec, ticker, ids, ct));

            outcome.Included.Add((c.Region, string.Create(CultureInfo.InvariantCulture,
                $"| {ticker} | {company.Name} | {c.Location.City}, {c.Location.State} | {exchange} | {company.Sector} | {Money(fin.LatestAnnualRevenue)} | {pay.Select(p => p.Name).Distinct().Count()} | {(pay.Count > 0 ? $"{pay.Min(p => p.Year)}–{pay.Max(p => p.Year)}" : "—")} |")));
            logger.LogInformation("{Ticker,-6} {Name}: {Periods} periods, {People} executives from {Proxies} proxies",
                ticker, company.Name, fin.Periods.Count, pay.Select(p => p.Name).Distinct().Count(), proxies);
        }

        // 4. Publish: this market's rows in the website's database (checked with the API's rules before it replaces the file).
        publisher.Publish(Market, outcome.Companies, outcome.Locations, outcome.Financials, outcome.Pay, outcome.People.Values.ToList(),
            Compensation.NewHireReader.LinkByUniqueName(outcome.NewExecutives, outcome.People.Keys),
            "SEC EDGAR: company submissions, XBRL company facts, DEF 14A summary compensation tables, insider (Form 3/4/5) owner lists. ZIP centroids: US Census Gazetteer. ZIP names: GeoNames (CC-BY 4.0).",
            string.Join("; ", o.Regions.Select(r => r.Name)), outcome.WorkerPay);
        var zips = await geo.WriteZipTableAsync(ct);
        await WriteReportAsync(outcome, zips, parsedProxies, ct);

        logger.LogInformation("Done: {Companies} companies, {Periods} financial periods, {Pay} pay rows for {People} people → {Path} ({Requests} network requests)",
            outcome.Companies.Count, outcome.Financials.Count, outcome.Pay.Count, outcome.People.Count, publisher.DatabasePath, client.NetworkRequests);
        return 0;
    }

    /// <summary>This importer's partition of the database: SEC filers (US, Canada, Australia, New Zealand).</summary>
    public const string Market = "sec";

    private sealed record PayRow(string Name, string Title, int Year, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, string Source);

    /// <summary>Pay rows for one company, and how they fared against the CEO totals the proxies tag (pay versus performance).</summary>
    private sealed record PayResult(List<PayRow> Rows, int Proxies, List<string> Warnings, int Verified, int Corrected, int Dropped, int Unmatched,
        List<(int Year, Compensation.PayRatio Ratio, string Source)> Ratios);

    private async Task<PayResult> ReadExecutivePayAsync(SecCompany sec, string ticker, int fromYear, CancellationToken ct)
    {
        var rows = new Dictionary<(string Person, int Year), PayRow>();
        var warnings = new List<string>();
        var pvp = new Dictionary<(DateOnly End, string? Name), PvpFact>();
        var proxies = sec.Filings.Where(f => f.Form == "DEF 14A").OrderByDescending(f => f.FilingDate).ToList();
        var read = 0;
        var covered = new HashSet<int>();
        var ratios = new List<(int Year, Compensation.PayRatio Ratio, string Source)>();

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
            // The CEO totals the company tagged; the newest proxy wins for any year two proxies both cover.
            foreach (var f in PayVersusPerformance.Read(html)) pvp.TryAdd((f.PeriodEnd, f.Name), f);
            var result = compensation.Parse(html);
            ReadPayRatio(html, proxy, result.Rows, sec.Cik, ratios);
            if (result.Rows.Count == 0) { warnings.Add($"proxy {proxy.FilingDate}: no compensation table recognised"); continue; }
            warnings.AddRange(result.Warnings.Take(3).Select(w => $"proxy {proxy.FilingDate}: {w}"));

            // A proxy covers the last fiscal year and a few before it; any other year is a misread cell (one table produced 2042).
            var impossible = result.Rows.Where(r => r.Year > proxy.FilingDate.Year || r.Year < proxy.FilingDate.Year - 5).ToList();
            if (impossible.Count > 0)
                warnings.Add($"proxy {proxy.FilingDate}: skipped {impossible.Count} row(s) with impossible years ({string.Join(", ", impossible.Select(r => r.Year).Distinct())})");

            // A table printed "in thousands" reads as a $689 CEO: when every total is that small, scale the whole table.
            var parsed = result.Rows.Except(impossible).ToList();
            if (parsed.Count > 0 && parsed.All(r => r.Total < 25_000) && parsed.Any(r => r.Total > 0))
            {
                parsed = parsed.Select(r => r with { Salary = r.Salary * 1000, Bonus = r.Bonus * 1000, StockAwards = r.StockAwards * 1000, Other = r.Other * 1000, Total = r.Total * 1000 }).ToList();
                warnings.Add($"proxy {proxy.FilingDate}: table read as thousands of dollars (every total was under $25,000)");
            }
            // Rows that can't be a person's pay: negative amounts, salary above the total, a label or company in the name
            // column, or a tiny total in an otherwise normal table (a misread footnote).
            var unusable = parsed.Where(r => r.Salary < 0 || r.Bonus < 0 || r.StockAwards < 0 || r.Total < 0 || (r.Total > 0 && r.Salary > r.Total * 1.02m)
                                             || !Validation.DataValidator.LooksLikePerson(r.Name) || r.Total is > 0 and < 10_000).ToList();
            if (unusable.Count > 0)
                warnings.Add($"proxy {proxy.FilingDate}: left out {unusable.Count} row(s) that can't be right ({string.Join("; ", unusable.Take(3).Select(r => $"{r.Name} {r.Year}: salary {r.Salary:N0}, total {r.Total:N0}"))})");

            foreach (var r in parsed.Where(r => r.Year >= fromYear).Except(unusable))
            {
                covered.Add(r.Year);
                // Newer proxies win: they're read first, so only add what's missing.
                rows.TryAdd((PersonId(r.Name), r.Year),
                    new PayRow(r.Name, r.Title, r.Year, r.Salary, r.Bonus, r.StockAwards, r.Other, r.Total, proxy.Url(sec.Cik)));
            }
        }
        var (verified, corrected, dropped, unmatched) = CheckAgainstPayVersusPerformance(rows, pvp.Values, warnings);
        return new PayResult(rows.Values.OrderBy(r => r.Name).ThenBy(r => r.Year).ToList(), read, warnings, verified, corrected, dropped, unmatched,
            ratios.OrderBy(r => r.Year).ToList());
    }

    /// <summary>
    /// The proxy's CEO pay ratio, for the latest year its pay table covers (the year before it was filed when the table
    /// wasn't read). Proxies are read newest first, so a year already found isn't replaced by an older proxy's.
    /// </summary>
    private static void ReadPayRatio(string html, SecFiling proxy, IReadOnlyList<CompRow> table, long cik,
        List<(int Year, Compensation.PayRatio Ratio, string Source)> ratios)
    {
        var plausible = table.Where(r => r.Year <= proxy.FilingDate.Year && r.Year >= proxy.FilingDate.Year - 2).ToList();
        var year = plausible.Count > 0 ? plausible.Max(r => r.Year) : proxy.FilingDate.Year - 1;
        if (ratios.Any(r => r.Year == year)) return;
        var ceo = plausible.Where(r => r.Year == year && Core.Services.ExecutiveRoles.Holds(r.Title, ExecutiveRole.Ceo)).Select(r => r.Total).ToList();
        var totals = ceo.Count > 0 ? ceo : plausible.Where(r => r.Year == year).Select(r => r.Total).ToList();
        if (Compensation.PayRatioReader.Read(html, totals) is { } ratio) ratios.Add((year, ratio, proxy.Url(cik)));
    }

    /// <summary>
    /// Compares each tagged CEO total with the table row for that person and year. A match verifies the row; the same person
    /// with a different total means the table parser misread it: the tagged total replaces it (or the row is dropped). A tagged
    /// CEO-year with no row at all is only counted (the table parser missed it).
    /// </summary>
    private static (int Verified, int Corrected, int Dropped, int Unmatched) CheckAgainstPayVersusPerformance(
        Dictionary<(string Person, int Year), PayRow> rows, IEnumerable<PvpFact> facts, List<string> warnings)
    {
        int verified = 0, corrected = 0, dropped = 0, unmatched = 0;
        static bool Close(decimal a, decimal b) => Math.Abs(a - b) <= Math.Max(1m, Math.Abs(b) * 0.001m);
        // Some companies tag dollars with a "thousands" scale (Amtech's $723,580 tagged as $723,580,000): the same digits
        // a power of 1,000 apart still confirm the table, which is right.
        static bool Same(decimal table, decimal tagged) =>
            Close(table, tagged) || Close(table * 1_000m, tagged) || Close(table * 1_000_000m, tagged) || Close(table, tagged * 1_000m);
        foreach (var f in facts.Where(f => f.Total > 0))
        {
            // A fiscal year ending early in the calendar year can be labelled either way; try both.
            var years = new[] { f.FiscalYear, f.PeriodEnd.Year }.Distinct().ToArray();
            var inYear = rows.Where(kv => years.Contains(kv.Key.Year)).ToList();
            if (inYear.Any(kv => Same(kv.Value.Total, f.Total))) { verified++; continue; }
            var same = f.Name is null ? [] : inYear.Where(kv => PayVersusPerformance.SamePerson(f.Name, kv.Value.Name)).ToList();
            if (same.Count == 1)
            {
                // The tagged total is the company's own figure: use it, and let "other" absorb the difference (the
                // parser already puts columns it can't place there). If that would make "other" negative, the row's
                // pieces are wrong too, so leave it out.
                var (key, row) = (same[0].Key, same[0].Value);
                // Only a nearby figure corrects the table; a tag far off (more than double, or under half) is itself wrong.
                if (row.Total > 0 && (f.Total > row.Total * 2 || f.Total < row.Total / 2))
                {
                    unmatched++;
                    warnings.Add($"{row.Name} {row.Year}: the filing tags the CEO's total as {f.Total:N0}, far from the table's {row.Total:N0}; kept the table figure");
                    continue;
                }
                var other = row.Other + (f.Total - row.Total);
                if (other >= 0)
                {
                    rows[key] = row with { Total = f.Total, Other = other };
                    corrected++;
                    warnings.Add($"{row.Name} {row.Year}: table read total {row.Total:N0}; the filing tags the CEO's total as {f.Total:N0} — corrected");
                }
                else
                {
                    rows.Remove(key);
                    dropped++;
                    warnings.Add($"{row.Name} {row.Year}: table read total {row.Total:N0}, but the filing tags the CEO's total as {f.Total:N0}; row left out");
                }
                continue;
            }
            unmatched++;
        }
        return (verified, corrected, dropped, unmatched);
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

    private static CompanyLocation HeadquartersLocation(SecCompany sec, SecAddress addr, PlacedAddress placed) => new()
    {
        LocationId = "HQ", CompanyId = sec.PrimaryTicker ?? sec.Cik10, Type = LocationType.Headquarters, Label = "Headquarters",
        Street = Text.TitleCase(addr.Street), City = placed.City ?? Text.TitleCase(addr.City), State = placed.State,
        PostalCode = placed.PostalCode, Point = placed.Point
    };

    /// <summary>Annual and quarterly reports: 40-F is the annual report of Canadian companies listed in the US.</summary>
    private static readonly HashSet<string> StillReportingForms = ["10-K", "10-Q", "10-K/A", "40-F", "40-F/A", "20-F"];

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
        sb.AppendLine($"Metros: **{options.Value.Regions.Count}**. Source: SEC EDGAR (submissions, XBRL company facts, DEF 14A, insider owner lists). ZIP centroids: US Census Gazetteer; ZIP names: GeoNames.").AppendLine();
        sb.AppendLine($"- Companies included: **{o.Companies.Count}**");
        sb.AppendLine($"- Financial periods: {o.Financials.Count}");
        sb.AppendLine($"- Executive pay rows: {o.Pay.Count} for {o.People.Count} people, from {proxies} proxy statements");
        sb.AppendLine($"- People linked by SEC insider id: {o.PeopleLinkedByCik}; matched by name within one company only: {o.PeopleByNameOnly}");
        sb.AppendLine($"- CEO pay checked against the pay-versus-performance totals companies tag in their proxies: {o.PayVerified} match, " +
                      $"{o.PayCorrected} misread totals corrected to the tagged figure, {o.PayDropped} rows left out, {o.PayUnmatched} tagged CEO-years with no table row");
        sb.AppendLine($"- ZIP table rows written: {zipCount}");
        sb.AppendLine($"- Network requests this run: {client.NetworkRequests} (the rest came from the local cache)").AppendLine();
        sb.AppendLine("| Metro | Companies |").AppendLine("|---|---|");
        foreach (var r in options.Value.Regions) sb.AppendLine($"| {r.Name} | {o.Included.Count(i => i.Region == r.Name)} |");
        sb.AppendLine();
        foreach (var r in options.Value.Regions.Select(r => r.Name).Append("Other"))
        {
            var lines = o.Included.Where(i => i.Region == r).Select(i => i.Line).Order().ToList();
            if (lines.Count == 0) continue;
            sb.AppendLine($"## Included — {r} ({lines.Count})").AppendLine();
            sb.AppendLine("| Ticker | Company | City | Exchange | Sector | Latest annual revenue | Executives | Pay years |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var line in lines) sb.AppendLine(line);
            sb.AppendLine();
        }
        sb.AppendLine("## Excluded").AppendLine();
        sb.AppendLine($"- {o.NoTicker} filers with no ticker (funds, trusts, shells, private companies with public debt)");
        sb.AppendLine($"- {o.NotReporting} listed companies that haven't filed an annual or quarterly report since {options.Value.Discovery.Since:yyyy-MM-dd}");
        sb.AppendLine($"- {o.Abroad} US-listed companies headquartered outside the US and Canada");
        sb.AppendLine($"- {o.OutsideMetros} listed companies in the searched states but outside the covered metros");
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
