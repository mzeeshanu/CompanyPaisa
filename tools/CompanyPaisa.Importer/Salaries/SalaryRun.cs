using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Enrichment;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Salaries;

/// <summary>Settings for --salaries (appsettings section "Importer:Salaries"). Paths are relative to the repo root.</summary>
public sealed class SalaryOptions
{
    /// <summary>The Department of Labor page that links the quarterly disclosure files.</summary>
    public string DisclosurePage { get; set; } = "https://www.dol.gov/agencies/eta/foreign-labor/performance";
    public string CacheDirectory { get; set; } = "data/cache/lca";
    public string AliasesPath { get; set; } = "data/reference/employer-aliases.csv";
    public string ReportPath { get; set; } = "data/import-report-salaries.md";
    /// <summary>How many federal fiscal years of filings to use: the latest published and the ones before it.</summary>
    [Range(1, 5)] public int FiscalYears { get; set; } = 2;
    /// <summary>A job title (or a title at one place) is shown only with at least this many filings, so no one person's pay stands out.</summary>
    [Range(2, 100)] public int MinFilings { get; set; } = 3;
    /// <summary>Yearly salaries outside this range are typing mistakes (an hourly rate entered as yearly, an extra zero).</summary>
    public decimal MinYearlyPay { get; set; } = 25_000;
    public decimal MaxYearlyPay { get; set; } = 2_000_000;
}

/// <summary>
/// --salaries: what public companies offer for each job title, from the H-1B (and E-3, H-1B1) labor condition applications
/// the US Department of Labor publishes every quarter. Every such filing states the job title, the work place and the
/// salary the employer will pay. Filings are matched to the companies in the database by tax id (EIN) or name, grouped by
/// job title, and summarised (25th percentile, median, 75th percentile) company-wide and per work place.
/// </summary>
public sealed partial class SalaryRun(IOptions<ImporterOptions> options, RepoPaths paths, IEdgarService edgar, ILoggerFactory loggers)
{
    private readonly SalaryOptions _o = options.Value.Salaries;
    private readonly ILogger _log = loggers.CreateLogger<SalaryRun>();

    private static readonly HashSet<string> Columns =
    [
        "CASE_NUMBER", "CASE_STATUS", "DECISION_DATE", "JOB_TITLE", "SOC_TITLE", "FULL_TIME_POSITION", "EMPLOYER_NAME", "EMPLOYER_FEIN",
        "WORKSITE_CITY", "WORKSITE_STATE", "WORKSITE_POSTAL_CODE", "WAGE_RATE_OF_PAY_FROM", "WAGE_UNIT_OF_PAY"
    ];

    /// <summary>One usable filing: matched to a company, certified, full time, with a plausible yearly salary.</summary>
    private sealed record Filing(string CompanyId, string Title, string? Occupation, string City, string State, string Zip, decimal Salary, DateOnly Decided);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var dbPath = paths.Resolve(options.Value.Output.DatabasePath);
        SqliteDataStore.Upgrade(dbPath);
        var data = SqliteDataStore.Read(dbPath, DateTimeOffset.UtcNow);
        var report = new Report();

        // 1. Who's who: every US-listed (SEC) company by tax id and name, plus the curated aliases.
        var matcher = new EmployerMatcher();
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2));
        foreach (var c in data.Companies.Where(c => EnrichmentRun.MarketOf(c) == "sec"))
        {
            string? ein = null;
            if (await edgar.FindCikByTickerAsync(c.Ticker, ct) is { } cik) ein = (await edgar.GetCompanyAsync(cik, since, ct))?.Ein;
            if (ein is not null) report.CompaniesWithEin++;
            matcher.AddCompany(c.CompanyId, c.Name, ein);
            report.Companies++;
        }
        foreach (var (employer, ticker) in ReadAliases())
        {
            if (data.Companies.FirstOrDefault(c => c.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase)) is { } c) matcher.AddAlias(employer, c.CompanyId);
            else report.Notes.Add($"Alias '{employer}' → {ticker}: no such company in the database.");
        }
        _log.LogInformation("{Companies} US-listed companies to match ({Ein} with a tax id)", report.Companies, report.CompaniesWithEin);

        // 2. The disclosure files: the latest fiscal years, downloaded once.
        var files = await DownloadAsync(ct);
        if (files.Count == 0) { _log.LogError("No disclosure files found at {Page}", _o.DisclosurePage); return 1; }

        // 3. Every filing, newest file first so an amended case keeps its latest decision.
        var filings = new Dictionary<string, Filing>(StringComparer.Ordinal);
        var employers = new Dictionary<(string, string), (string CompanyId, string How)?>();
        var unmatched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var before = report.Rows;
            foreach (var row in XlsxRows.Read(file, Columns))
            {
                report.Rows++;
                if (report.Rows % 250_000 == 0) _log.LogInformation("{Rows:N0} filings read, {Kept:N0} kept", report.Rows, filings.Count);
                var caseNumber = row.GetValueOrDefault("CASE_NUMBER", "");
                if (caseNumber.Length == 0 || filings.ContainsKey(caseNumber)) continue;
                if (!row.GetValueOrDefault("CASE_STATUS", "").Equals("Certified", StringComparison.OrdinalIgnoreCase)) continue;
                if (!row.GetValueOrDefault("FULL_TIME_POSITION", "").StartsWith('Y')) continue;
                report.Certified++;

                var employer = row.GetValueOrDefault("EMPLOYER_NAME", "").Trim();
                var fein = row.GetValueOrDefault("EMPLOYER_FEIN", "");
                if (!employers.TryGetValue((employer, fein), out var match)) employers[(employer, fein)] = match = matcher.Match(employer, fein);
                if (match is not { } m)
                {
                    unmatched[employer] = unmatched.GetValueOrDefault(employer) + 1;
                    continue;
                }
                if (Yearly(row.GetValueOrDefault("WAGE_RATE_OF_PAY_FROM"), row.GetValueOrDefault("WAGE_UNIT_OF_PAY")) is not { } salary ||
                    salary < _o.MinYearlyPay || salary > _o.MaxYearlyPay)
                {
                    report.ImplausiblePay++;
                    continue;
                }
                var title = row.GetValueOrDefault("JOB_TITLE", "").Trim();
                if (title.Length == 0 || JobTitles.Key(title).Length == 0) continue;
                var decided = double.TryParse(row.GetValueOrDefault("DECISION_DATE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                    ? DateOnly.FromDateTime(DateTime.FromOADate(serial)) : DateOnly.MinValue;
                filings[caseNumber] = new Filing(m.CompanyId, title, Clean(row.GetValueOrDefault("SOC_TITLE")),
                    Geo.Text.TitleCase(row.GetValueOrDefault("WORKSITE_CITY", "").Trim()), row.GetValueOrDefault("WORKSITE_STATE", "").Trim().ToUpperInvariant(),
                    Zip5(row.GetValueOrDefault("WORKSITE_POSTAL_CODE")), salary, decided);
                report.MatchedBy[m.How] = report.MatchedBy.GetValueOrDefault(m.How) + 1;
            }
            report.Files.Add($"{Path.GetFileName(file)}: {report.Rows - before:N0} filings");
            _log.LogInformation("{File}: {Rows:N0} filings read; {Kept:N0} kept so far", Path.GetFileName(file), report.Rows - before, filings.Count);
        }

        // 4. Salaries by job title, company-wide and per work place.
        var zips = ReadZipCentroids();
        var rows = Summarise(filings.Values, zips);
        var used = filings.Values.Where(f => f.Decided != DateOnly.MinValue).ToList();
        var source = new JobSalarySource(used.Min(f => f.Decided), used.Max(f => f.Decided),
            "US Department of Labor, H-1B labor condition applications (disclosure data)");
        SqliteDataStore.ReplaceJobSalaries(dbPath, rows, source);

        report.Filings = filings.Count;
        report.Unmatched = unmatched;
        await WriteReportAsync(report, rows, filings.Values, source, ct);
        _log.LogInformation("Job salaries: {Titles:N0} job titles at {Companies:N0} companies ({Places:N0} work places) from {Filings:N0} filings, {From} to {To} → {Path}",
            rows.Count(r => r.City is null), rows.Select(r => r.CompanyId).Distinct().Count(), rows.Count(r => r.City is not null), filings.Count,
            source.From, source.To, dbPath);
        return 0;
    }

    /// <summary>Titles with enough filings, each with its work places that have enough filings themselves.</summary>
    private List<JobSalary> Summarise(IEnumerable<Filing> filings, IReadOnlyDictionary<string, GeoPoint> zips)
    {
        var rows = new List<JobSalary>();
        foreach (var job in filings.GroupBy(f => (f.CompanyId, Key: JobTitles.Key(f.Title))).Where(g => g.Count() >= _o.MinFilings)
                     .OrderBy(g => g.Key.CompanyId, StringComparer.Ordinal).ThenByDescending(g => g.Count()))
        {
            var title = JobTitles.Display(job.Select(f => f.Title));
            var occupation = job.Select(f => f.Occupation).OfType<string>().GroupBy(o => o).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
            rows.Add(Stats(job.Key.CompanyId, title, occupation, null, null, null, job.Select(f => f.Salary)));
            foreach (var place in job.GroupBy(f => (f.City, f.State)).Where(g => g.Key.City.Length > 0 && g.Count() >= _o.MinFilings).OrderByDescending(g => g.Count()))
            {
                var zip = place.GroupBy(f => f.Zip).OrderByDescending(g => g.Count()).First().Key;
                rows.Add(Stats(job.Key.CompanyId, title, occupation, place.Key.City, place.Key.State, zips.TryGetValue(zip, out var p) ? p : null, place.Select(f => f.Salary)));
            }
        }
        return rows;
    }

    private static JobSalary Stats(string companyId, string title, string? occupation, string? city, string? state, GeoPoint? point, IEnumerable<decimal> salaries)
    {
        var s = salaries.Order().ToArray();
        return new JobSalary
        {
            CompanyId = companyId, Title = title, Occupation = occupation, City = city, State = state, Point = point, Filings = s.Length,
            Min = s[0], Low = Percentile(s, 0.25), Median = Percentile(s, 0.5), High = Percentile(s, 0.75), Max = s[^1]
        };
    }

    /// <summary>Linear interpolation between the closest ranks, to the dollar.</summary>
    internal static decimal Percentile(decimal[] sorted, double p)
    {
        var at = (sorted.Length - 1) * p;
        var lower = (int)Math.Floor(at);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return Math.Round(sorted[lower] + (sorted[upper] - sorted[lower]) * (decimal)(at - lower), 0);
    }

    /// <summary>
    /// The offered rate as a yearly salary to the dollar (a full-time year is 2,080 hours). Whole dollars, so percentiles
    /// (rounded to the dollar) can't fall outside the lowest and highest offers.
    /// </summary>
    internal static decimal? Yearly(string? rate, string? unit)
    {
        if (!decimal.TryParse((rate ?? "").Replace("$", "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var r) || r <= 0) return null;
        decimal? yearly = (unit ?? "").Trim().ToLowerInvariant() switch
        {
            "year" => r,
            "hour" => r * 2080,
            "week" => r * 52,
            "bi-weekly" => r * 26,
            "month" => r * 12,
            _ => null
        };
        return yearly is { } y ? Math.Round(y, 0) : null;
    }

    private static string Zip5(string? zip) => (zip ?? "").Trim() is { Length: >= 5 } z && z[..5].All(char.IsDigit) ? z[..5] : "";

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : Regex.Replace(s.Trim(), @"\s+", " ");

    // ---- Files -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The disclosure files of the latest <see cref="SalaryOptions.FiscalYears"/> fiscal years, newest first, downloaded into
    /// the cache (each is 80–250 MB; a file already there is kept, since a published quarter never changes).
    /// </summary>
    private async Task<List<string>> DownloadAsync(CancellationToken ct)
    {
        using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", CareersFinder.UserAgent);
        var page = await http.GetStringAsync(_o.DisclosurePage, ct);
        var links = DisclosureLink().Matches(page)
            .Select(m => (Url: new Uri(new Uri(_o.DisclosurePage), m.Groups["href"].Value).AbsoluteUri, Year: int.Parse(m.Groups["fy"].Value, CultureInfo.InvariantCulture), Quarter: int.Parse(m.Groups["q"].Value, CultureInfo.InvariantCulture)))
            .DistinctBy(l => (l.Year, l.Quarter)).ToList();
        if (links.Count == 0) return [];
        var latest = links.Max(l => l.Year);
        var wanted = links.Where(l => l.Year > latest - _o.FiscalYears).OrderByDescending(l => l.Year).ThenByDescending(l => l.Quarter).ToList();

        var dir = paths.Resolve(_o.CacheDirectory);
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var (url, year, quarter) in wanted)
        {
            var file = Path.Combine(dir, $"LCA_Disclosure_Data_FY{year}_Q{quarter}.xlsx");
            if (!File.Exists(file))
            {
                _log.LogInformation("Downloading {Url}", url);
                var partial = file + ".partial";
                await using (var output = File.Create(partial))
                await using (var input = await http.GetStreamAsync(url, ct))
                    await input.CopyToAsync(output, ct);
                File.Move(partial, file, overwrite: true);
            }
            files.Add(file);
        }
        return files;
    }

    [GeneratedRegex(@"href=""(?<href>[^""]*LCA_Disclosure_Data_FY(?<fy>\d{4})_Q(?<q>[1-4])\.xlsx)""", RegexOptions.IgnoreCase)]
    private static partial Regex DisclosureLink();

    // ---- Reference tables ------------------------------------------------------------------------------------------

    private IEnumerable<(string Employer, string Ticker)> ReadAliases()
    {
        var path = paths.Resolve(_o.AliasesPath);
        if (!File.Exists(path)) yield break;
        // employer,ticker,note
        foreach (var row in Csv.Read(path).Skip(1))
            if (row.Count >= 2 && row[0].Trim() is { Length: > 0 } employer && row[1].Trim() is { Length: > 0 } ticker)
                yield return (employer, ticker);
    }

    private Dictionary<string, GeoPoint> ReadZipCentroids()
    {
        var zips = new Dictionary<string, GeoPoint>(StringComparer.Ordinal);
        var path = paths.Resolve(options.Value.Geo.ZipTableOutput);
        if (!File.Exists(path)) return zips;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length >= 5 && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                zips.TryAdd(f[0], new GeoPoint(lat, lng));
        }
        return zips;
    }

    // ---- Report ----------------------------------------------------------------------------------------------------

    private sealed class Report
    {
        public int Companies { get; set; }
        public int CompaniesWithEin { get; set; }
        public int Rows { get; set; }
        public int Certified { get; set; }
        public int ImplausiblePay { get; set; }
        public int Filings { get; set; }
        public Dictionary<string, int> MatchedBy { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Unmatched { get; set; } = [];
        public List<string> Files { get; } = [];
        public List<string> Notes { get; } = [];
    }

    private async Task WriteReportAsync(Report r, List<JobSalary> rows, IEnumerable<Filing> filings, JobSalarySource source, CancellationToken ct)
    {
        var titles = rows.Where(x => x.City is null).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Job salaries import report").AppendLine();
        sb.AppendLine($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `--salaries`. Source: {source.Source}, decisions {source.From:yyyy-MM-dd} to {source.To:yyyy-MM-dd}.").AppendLine();
        sb.AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine($"| US-listed companies | {r.Companies:N0} ({r.CompaniesWithEin:N0} with a tax id from the SEC) |");
        sb.AppendLine($"| Filings read | {r.Rows:N0} |");
        sb.AppendLine($"| Certified, full-time | {r.Certified:N0} |");
        sb.AppendLine($"| Matched to a company | {r.Filings:N0} ({string.Join(", ", r.MatchedBy.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"))}) |");
        sb.AppendLine($"| Left out: implausible yearly salary | {r.ImplausiblePay:N0} |");
        sb.AppendLine($"| Companies with job salaries | {titles.Select(t => t.CompanyId).Distinct().Count():N0} |");
        sb.AppendLine($"| Job titles (at least {_o.MinFilings} filings) | {titles.Count:N0} |");
        sb.AppendLine($"| Work-place rows | {rows.Count - titles.Count:N0} |").AppendLine();
        sb.AppendLine("## Files").AppendLine();
        foreach (var f in r.Files) sb.AppendLine($"- {f}");
        sb.AppendLine().AppendLine("## Most filings").AppendLine().AppendLine("| Company | Filings | Titles |").AppendLine("|---|---:|---:|");
        foreach (var g in filings.GroupBy(f => f.CompanyId).OrderByDescending(g => g.Count()).Take(40))
            sb.AppendLine($"| {g.Key} | {g.Count():N0} | {titles.Count(t => t.CompanyId == g.Key):N0} |");
        sb.AppendLine().AppendLine("## Employers not matched to a company (most filings first)").AppendLine();
        sb.AppendLine("Mostly private companies. A public company's subsidiary here can be added to `data/reference/employer-aliases.csv`.").AppendLine();
        sb.AppendLine("| Employer | Filings |").AppendLine("|---|---:|");
        foreach (var (employer, n) in r.Unmatched.OrderByDescending(kv => kv.Value).Take(150)) sb.AppendLine($"| {employer.Replace("|", "/")} | {n:N0} |");
        if (r.Notes.Count > 0)
        {
            sb.AppendLine().AppendLine("## Notes").AppendLine();
            foreach (var n in r.Notes) sb.AppendLine($"- {n}");
        }
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }
}
