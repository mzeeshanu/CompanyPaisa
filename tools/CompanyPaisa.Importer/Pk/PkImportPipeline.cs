using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Pk;

/// <summary>
/// Builds the Pakistan market of the database from free, public sources:
/// the Pakistan Stock Exchange's list of listed companies and their profiles (registered address, chief executive,
/// website) → the town in the address (GeoNames) → the company's own annual reports (PDFs filed with the exchange),
/// read by rules for two or more years of revenue and profit and the chief executive's pay. Scanned (image-only)
/// reports can't be read and are left out, as are companies without a recent report.
/// </summary>
public sealed partial class PkImportPipeline(IOptions<ImporterOptions> options, RepoPaths paths, Publishing.DataPublisher publisher, ILoggerFactory loggers)
    : Publishing.IMarketImporter
{
    string Publishing.IMarketImporter.Market => Market;
    public string Description => "Pakistan Stock Exchange: companies' own annual reports (revenue, profit, chief executive's pay)";
    Task<int> Publishing.IMarketImporter.RunAsync(bool refreshLists, CancellationToken ct) => RunAsync(ct);

    /// <summary>This importer's partition of the database.</summary>
    public const string Market = "pk";

    private readonly PkOptions _o = options.Value.Pk;
    private readonly ILogger _log = loggers.CreateLogger<PkImportPipeline>();
    private readonly HaversineDistanceCalculator _distance = new();

    private sealed record Symbol(string Code, string Name, string Sector);
    private sealed record Announcement(DateOnly Date, string Title, string DocumentId);
    private sealed record ReadReport(Announcement Source, PkAnnualReport Report, bool Scanned);
    private sealed record Found(Symbol Symbol, PsxProfile Profile, (string City, string Province, string PostalCode, GeoPoint Point)? Place,
        List<ReadReport> Reports, string? Problem);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        using var client = new SecClient(new SecOptions
        {
            UserAgent = _o.UserAgent, MaxRequestsPerSecond = _o.MaxRequestsPerSecond, CacheDirectory = _o.CacheDirectory,
            IndexCacheHours = _o.IndexCacheHours, CompressCache = true
        }, paths, loggers.CreateLogger<SecClient>());
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _o.UserAgent);

        var places = new PkPlaces();
        await places.LoadAsync(client, _o.CitiesUrl, _o.PostcodesUrl, ct);
        var symbols = await ListAsync(client, ct);
        _log.LogInformation("Pakistan: {Count} listed companies to check", symbols.Count);

        var results = new ConcurrentBag<Found>();
        var done = 0;
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = _o.Parallelism, CancellationToken = ct }, async (s, token) =>
        {
            results.Add(await ReadCompanyAsync(client, http, places, s, token));
            var n = Interlocked.Increment(ref done);
            if (n % 50 == 0) _log.LogInformation("{Done}/{Total} companies read", n, symbols.Count);
        });

        var companies = new List<Company>();
        var locations = new List<CompanyLocation>();
        var periods = new List<FinancialPeriod>();
        var pay = new List<ExecutiveCompensation>();
        var people = new Dictionary<string, Person>(StringComparer.OrdinalIgnoreCase);
        var included = new List<(string Region, string Line)>();
        var excluded = new List<string>();
        var warnings = new List<string>();
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-_o.RecentMonths);

        foreach (var f in results.OrderBy(r => r.Symbol.Code, StringComparer.Ordinal))
        {
            var companyId = f.Symbol.Code + _o.TickerSuffix;
            var label = $"{companyId} {f.Symbol.Name}";
            if (f.Problem is not null) { excluded.Add($"{label}: {f.Problem}"); continue; }
            var latest = f.Reports.FirstOrDefault(r => r.Report.Main is not null);
            if (latest is null)
            {
                excluded.Add($"{label}: " + (f.Reports.Count == 0 ? "no annual report filed with the exchange"
                    : f.Reports.All(r => r.Scanned) ? "annual report is a scan (no text to read)" : "no statement of profit or loss found in the annual report"));
                continue;
            }
            var main = latest.Report.Main!;
            var yearEnd = main.PeriodEnd ?? YearEnd(main.FiscalYear, f.Profile.FiscalYearEndMonthDay());
            if (yearEnd is null || yearEnd < cutoff) { excluded.Add($"{label}: latest annual report is for FY{main.FiscalYear} (not recent)"); continue; }
            // Where the company is run: the head office in its annual report when that's in another town than the
            // registered office the exchange lists (Lucky Cement: registered at its plant in Pezu, run from Karachi).
            var headOffice = latest.Report.HeadOffice is { } ho ? places.Locate(ho) : null;
            var useHeadOffice = headOffice is { } h && (f.Place is not { } r || !string.Equals(h.City, r.City, StringComparison.OrdinalIgnoreCase));
            if ((useHeadOffice ? headOffice : f.Place) is not { } place) { excluded.Add($"{label}: town not found in the address \"{f.Profile.Address}\""); continue; }
            var address = useHeadOffice ? latest.Report.HeadOffice! : f.Profile.Address!;

            // Years: the latest report's two, then each older report's (same kind of accounts: group or company).
            var years = new SortedDictionary<int, FinancialPeriod>();
            var source = Document(latest.Source.DocumentId);
            void Add(int year, decimal revenue, decimal netIncome, decimal? eps, string src)
            {
                if (revenue <= 0 || years.ContainsKey(year)) return;
                years[year] = new FinancialPeriod
                {
                    CompanyId = companyId, PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = netIncome,
                    Eps = eps, SourceFiling = src
                };
            }
            var scale = UnitCheck(main, f.Profile);
            if (scale != 1) warnings.Add($"{label}: statement says rupees but its figures are in thousands (checked against the exchange's share count or market value); multiplied by 1,000");
            Add(main.FiscalYear, main.Revenue * scale, main.NetIncome * scale, main.Eps, source);
            Add(main.FiscalYear - 1, main.PriorRevenue * scale, main.PriorNetIncome * scale, main.PriorEps, source);
            foreach (var older in f.Reports.Where(r => r != latest))
            {
                var s = older.Report.Statements.FirstOrDefault(x => x.Consolidated == main.Consolidated);
                if (s is null || s.FiscalYear >= main.FiscalYear) continue;
                var olderScale = UnitCheck(s, f.Profile);
                Add(s.FiscalYear, s.Revenue * olderScale, s.NetIncome * olderScale, s.Eps, Document(older.Source.DocumentId));
                Add(s.FiscalYear - 1, s.PriorRevenue * olderScale, s.PriorNetIncome * olderScale, s.PriorEps, Document(older.Source.DocumentId));
            }
            // A restated or misread year: revenue jumping 20× from one year to the next is flagged for review.
            var jumps = years.Values.Zip(years.Values.Skip(1)).Where(p => p.Second.Revenue > p.First.Revenue * 20 || p.First.Revenue > p.Second.Revenue * 20).ToList();
            if (jumps.Count > 0) warnings.Add($"{label}: revenue changes more than 20× between FY{jumps[0].First.FiscalYear} and FY{jumps[0].Second.FiscalYear}");

            var region = RegionFor(place.Point);
            companies.Add(new Company
            {
                CompanyId = companyId, Ticker = companyId, Name = TidyName(f.Symbol.Name), Exchange = _o.Exchange,
                Sector = SectorOf(f.Symbol.Sector), Industry = TitleCase(f.Symbol.Sector), Website = WebsiteOf(f.Profile.Website),
                Description = f.Profile.Description, Currency = "PKR",
                FiscalYearEnd = yearEnd.Value.ToString("MM-dd", CultureInfo.InvariantCulture), AsOfDate = yearEnd
            });
            locations.Add(new CompanyLocation
            {
                LocationId = $"{companyId}-HQ", CompanyId = companyId, Type = LocationType.Headquarters, Label = useHeadOffice ? "Head office" : "Registered office",
                Street = StreetOf(address, place.City), City = place.City, State = "PK", PostalCode = place.PostalCode, Point = place.Point
            });
            periods.AddRange(years.Values);

            var payNote = "";
            if (latest.Report.CeoPay is { } ceoPay && f.Profile.Ceo is { } ceo)
            {
                var personId = $"{PersonSlug(ceo)}-{f.Symbol.Code.ToLowerInvariant()}-ka";
                people.TryAdd(personId, new Person { PersonId = personId, Name = PersonName(ceo) });
                pay.Add(new ExecutiveCompensation
                {
                    CompanyId = companyId, PersonId = personId, ExecutiveName = PersonName(ceo), Title = "Chief Executive Officer", Year = ceoPay.FiscalYear,
                    Salary = ceoPay.Salary, Bonus = ceoPay.Bonus, Other = ceoPay.Total - ceoPay.Salary - ceoPay.Bonus, Total = ceoPay.Total, SourceFiling = source
                });
                payNote = $"Rs {ceoPay.Total / 1_000_000:0.#}M";
            }
            warnings.AddRange(latest.Report.Warnings.Select(w => $"{label}: {w}"));
            included.Add((region, string.Create(CultureInfo.InvariantCulture,
                $"| {companyId} | {TidyName(f.Symbol.Name)} | {place.City} | {(main.Consolidated ? "group" : "company")} | {Money(main.Revenue * scale)} | {years.Keys.Min()}–{years.Keys.Max()} | {payNote} |")));
        }

        publisher.Publish(Market, companies, locations, periods, pay, people.Values.ToList(), [],
            "Company list and profiles from the Pakistan Stock Exchange (dps.psx.com.pk); figures read from each company's annual report; towns and postcodes from GeoNames.",
            "Pakistan");
        var codes = await places.WriteTableAsync(paths.Resolve(_o.PostcodeTableOutput), ct);
        await WriteReportAsync(symbols.Count, companies.Count, periods.Count, pay.Count, codes, included, excluded, warnings, client.NetworkRequests, ct);
        _log.LogInformation("Done: {Companies} Pakistani companies, {Periods} years of figures, {Pay} chief executives' pay → {Path}",
            companies.Count, periods.Count, pay.Count, publisher.DatabasePath);
        return 0;
    }

    // ---- The exchange --------------------------------------------------------------------------------------------

    /// <summary>Listed equities: not ETFs, debt, funds or modarabas, rights (R) or other share classes.</summary>
    private async Task<List<Symbol>> ListAsync(ISecClient client, CancellationToken ct)
    {
        var json = await client.GetStringAsync($"{_o.PsxBase}/symbols", CachePolicy.Index, ct) ?? throw new InvalidOperationException("Couldn't download the PSX symbol list.");
        using var doc = JsonDocument.Parse(json);
        var excluded = _o.ExcludedSectors.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var only = _o.OnlySymbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return doc.RootElement.EnumerateArray()
            .Where(e => !e.GetProperty("isETF").GetBoolean() && !e.GetProperty("isDebt").GetBoolean())
            .Select(e => new Symbol(e.GetProperty("symbol").GetString() ?? "", e.GetProperty("name").GetString() ?? "", e.GetProperty("sectorName").GetString() ?? ""))
            .Where(s => s.Code.Length > 0 && s.Name.Length > 0 && !excluded.Contains(s.Sector) && !NotAnOrdinaryShare().IsMatch(s.Name)
                        && !s.Name.Contains("Modaraba", StringComparison.OrdinalIgnoreCase) && !s.Name.Contains(" Fund", StringComparison.OrdinalIgnoreCase))
            .Where(s => only.Count == 0 || only.Contains(s.Code))
            .DistinctBy(s => s.Code).ToList();
    }

    private async Task<Found> ReadCompanyAsync(ISecClient client, HttpClient http, PkPlaces places, Symbol s, CancellationToken ct)
    {
        var html = await client.GetStringAsync($"{_o.PsxBase}/company/{Uri.EscapeDataString(s.Code)}", CachePolicy.Index, ct);
        if (html is null) return new Found(s, PsxProfile.Parse(""), null, [], "no profile page on the exchange");
        var profile = PsxProfile.Parse(html);
        var place = profile.Address is null ? null : places.Locate(profile.Address);

        var reports = new List<ReadReport>();
        var readable = 0;
        if (await AnnualReportsAsync(http, s.Code, ct) is not { } announcements)
            return new Found(s, profile, place, [], "the exchange didn't answer the announcement search (try again later)");
        foreach (var a in announcements.Take(_o.ReportsPerCompany + 2))
        {
            var lines = await ReportLinesAsync(client, a.DocumentId, ct);
            PkAnnualReport report;
            try { report = AnnualReportReader.Read(lines); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A layout the rules trip over: that report is skipped, the run goes on.
                _log.LogWarning("{Symbol}: couldn't read report {Id} ({Message})", s.Code, a.DocumentId, ex.Message);
                report = new PkAnnualReport([], null, null, [$"report {a.DocumentId} couldn't be read: {ex.Message}"]);
            }
            reports.Add(new ReadReport(a, report, lines.Count < 100));
            // Stop at a scan or unreadable latest report only after trying the next one (sometimes a second copy).
            if (report.Main is not null && ++readable >= _o.ReportsPerCompany) break;
            if (reports.Count >= 2 && readable == 0) break;
        }
        return new Found(s, profile, place, reports, null);
    }

    /// <summary>The company's annual reports filed with the exchange, newest first (an announcement search on the portal).</summary>
    /// <returns>Null when the exchange didn't answer.</returns>
    private async Task<List<Announcement>?> AnnualReportsAsync(HttpClient http, string symbol, CancellationToken ct)
    {
        var file = Path.Combine(paths.Resolve(_o.CacheDirectory), "announcements", $"{symbol}.html");
        string html;
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromHours(_o.IndexCacheHours))
            html = await File.ReadAllTextAsync(file, ct);
        else
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["type"] = "C", ["symbol"] = symbol, ["query"] = "Annual Report", ["count"] = "50", ["offset"] = "0",
                ["date_from"] = "", ["date_to"] = "", ["page"] = "annc"
            });
            string? answer = null;
            for (var attempt = 1; attempt <= 5 && answer is null; attempt++)
            {
                try
                {
                    await Throttle(ct);
                    using var response = await http.PostAsync($"{_o.PsxBase}/announcements", form, ct);
                    response.EnsureSuccessStatusCode();
                    answer = await response.Content.ReadAsStringAsync(ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    // Too many requests or a busy server: wait longer each time (15 s, 30 s, …).
                    _log.LogWarning("Announcements for {Symbol} failed ({Message}); attempt {Attempt} of 5", symbol, ex.Message, attempt);
                    if (attempt < 5) await Task.Delay(TimeSpan.FromSeconds(attempt * 15), ct);
                }
            }
            // Never cache a failure: the company would look like it files no reports until the cache expires.
            if (answer is null) return null;
            html = answer;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, html, ct);
        }

        var list = new List<Announcement>();
        foreach (Match row in TableRow().Matches(html))
        {
            var cells = TableCell().Matches(row.Value).Select(c => WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", " ")).Trim()).ToList();
            var pdf = PdfLink().Match(row.Value);
            if (cells.Count < 5 || !pdf.Success) continue;
            var title = Regex.Replace(cells[4], @"\s+", " ");
            if (!AnnualReportTitle().IsMatch(title) || NotAnnual().IsMatch(title)) continue;
            if (!DateOnly.TryParseExact(cells[0], "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            list.Add(new Announcement(date, title, pdf.Groups["id"].Value));
        }
        return list.OrderByDescending(a => a.Date).DistinctBy(a => a.DocumentId).ToList();
    }

    /// <summary>
    /// A report's text lines. PDFs are large (5–30 MB) and never change once filed, so only the extracted lines are
    /// cached (gzip), not the PDF. A scan gives no lines, which is cached too.
    /// </summary>
    private async Task<IReadOnlyList<PdfTextLine>> ReportLinesAsync(ISecClient client, string documentId, CancellationToken ct)
    {
        var file = Path.Combine(paths.Resolve(_o.CacheDirectory), "reports", $"{documentId}.lines.gz");
        if (File.Exists(file))
        {
            try
            {
                await using var input = new GZipStream(File.OpenRead(file), CompressionMode.Decompress);
                using var reader = new StreamReader(input, Encoding.UTF8);
                var cached = new List<PdfTextLine>();
                while (await reader.ReadLineAsync(ct) is { } line) cached.Add(PdfTextLine.Deserialise(line));
                return cached;
            }
            catch (InvalidDataException) { File.Delete(file); }   // a damaged cache file: read the report again
        }
        var pdf = await client.GetBytesAsync(Document(documentId), CachePolicy.NoStore, ct);
        if (pdf is null) return [];
        var lines = PdfLines.Read(pdf);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        // Written beside and then renamed, so a run stopped half-way never leaves a cut-off file behind.
        var partial = file + ".partial";
        await using (var output = new GZipStream(File.Create(partial), CompressionLevel.SmallestSize))
        await using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
        {
            foreach (var line in lines) await writer.WriteLineAsync(line.Serialise().Replace('\n', ' ').Replace('\r', ' '));
            if (lines.Count == 0) await writer.WriteLineAsync();   // a scan: one blank line, so the file is a complete gzip
        }
        File.Move(partial, file, overwrite: true);
        return lines;
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _next = DateTime.MinValue;
    private async Task Throttle(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if (_next > now) await Task.Delay(_next - now, ct);
            _next = DateTime.UtcNow.AddMilliseconds(1000.0 / _o.MaxRequestsPerSecond);
        }
        finally { _gate.Release(); }
    }

    private string Document(string id) => $"{_o.PsxBase}/download/document/{id}.pdf";

    /// <summary>
    /// 1 when the statement's amounts are in rupees as read; 1,000 when a statement headed "Rupees" is really in thousands
    /// (the "'000" lost in the PDF). Checked against the exchange's profile: earnings per share × shares should be about the
    /// profit; without earnings per share, no listed company is worth over 500 times its yearly sales.
    /// </summary>
    public static decimal UnitCheck(PkStatement s, PsxProfile profile)
    {
        if (s.Eps is { } eps && eps != 0 && profile.Shares is { } shares && shares > 0 && s.NetIncome != 0)
        {
            var ratio = eps * shares / s.NetIncome;
            return ratio is > 300 and < 3000 ? 1000 : 1;
        }
        if (profile.MarketCapThousands is { } cap && cap > 0 && s.Revenue > 0)
            return cap * 1000 / s.Revenue > 500 ? 1000 : 1;
        return 1;
    }

    // ---- Tidying -------------------------------------------------------------------------------------------------

    /// <summary>The exchange's sectors mapped to the website's broad sectors (the exchange's own name is kept as the industry).</summary>
    public static string SectorOf(string psxSector)
    {
        var s = psxSector.ToUpperInvariant();
        (string Keyword, string Sector)[] map =
        [
            ("BANK", "Finance"), ("INSURANCE", "Finance"), ("INV.", "Finance"), ("LEASING", "Finance"), ("MODARABA", "Finance"),
            ("REAL ESTATE", "Real estate"), ("PROPERTY", "Real estate"),
            ("PHARMA", "Healthcare"),
            ("TECHNOLOGY", "Software & IT"),
            ("OIL & GAS", "Energy & utilities"), ("REFINERY", "Energy & utilities"), ("POWER", "Energy & utilities"),
            ("CEMENT", "Materials"), ("CHEMICAL", "Materials"), ("FERTILIZER", "Materials"), ("PAPER", "Materials"), ("GLASS", "Materials"),
            ("ENGINEERING", "Industrials"), ("CABLE", "Industrials"), ("AUTOMOBILE PARTS", "Industrials"),
            ("TRANSPORT", "Transportation"),
            ("AUTOMOBILE ASSEMBLER", "Consumer & retail"), ("FOOD", "Consumer & retail"), ("SUGAR", "Consumer & retail"), ("TOBACCO", "Consumer & retail"),
            ("VANASPATI", "Consumer & retail"), ("TEXTILE", "Consumer & retail"), ("SYNTHETIC", "Consumer & retail"), ("WOOLLEN", "Consumer & retail"),
            ("JUTE", "Consumer & retail"), ("LEATHER", "Consumer & retail"), ("APPAREL", "Consumer & retail")
        ];
        return map.FirstOrDefault(m => s.Contains(m.Keyword, StringComparison.Ordinal)).Sector ?? "Other";
    }

    private string RegionFor(GeoPoint point)
    {
        foreach (var r in _o.Regions)
            if (r.Anchors.Any(a => _distance.DistanceMiles(point, new GeoPoint(a.Latitude, a.Longitude)) <= a.RadiusMiles)) return r.Name;
        return _o.Regions.FirstOrDefault(r => r.WholeCountry)?.Name ?? "Pakistan";
    }

    private static DateOnly? YearEnd(int fiscalYear, string? monthDay) =>
        monthDay is null ? null : DateOnly.ParseExact($"{fiscalYear}-{monthDay}", "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The address before the town: "CDC House, 99-B, Block-B, S.M.C.H.S Main Shahra-e-Faisal".</summary>
    private static string StreetOf(string address, string city)
    {
        var at = address.LastIndexOf(city, StringComparison.OrdinalIgnoreCase);
        var street = (at > 0 ? address[..at] : address).Trim().TrimEnd(',', '-', ' ');
        return street.Length > 160 ? street[..160] : street;
    }

    private static string? WebsiteOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        return u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : "https://" + u;
    }

    /// <summary>"Lucky Cement Limited" stays; "LUCKY CEMENT LIMITED" becomes "Lucky Cement Limited".</summary>
    private static string TidyName(string name) => TitleCase(Regex.Replace(name, @"\s+", " ").Trim());

    private static string TitleCase(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower) ? s.Trim() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    /// <summary>"MR. MUHAMMAD ALI TABBA" → "Muhammad Ali Tabba".</summary>
    private static string PersonName(string name) => TitleCase(Regex.Replace(name, @"^(mr|mrs|ms|miss|dr|syed\s+mr)\.?\s+", "", RegexOptions.IgnoreCase).Trim());

    private static string PersonSlug(string name)
    {
        var n = Regex.Replace(name.ToLowerInvariant(), @"\b(mr|mrs|ms|miss|dr|engr|lt|gen|retd|capt)\b\.?", " ");
        var words = Regex.Replace(n, @"[^a-z\s-]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
        return words.Count >= 2 ? $"{words[0]}-{words[^1]}" : string.Join('-', words);
    }

    private static string Money(decimal v) =>
        v >= 1_000_000_000 ? $"Rs {v / 1_000_000_000:0.0}B" : v >= 1_000_000 ? $"Rs {v / 1_000_000:0}M" : $"Rs {v / 1_000:0}K";

    private async Task WriteReportAsync(int listed, int companies, int periods, int pay, int codes, List<(string Region, string Line)> included,
        List<string> excluded, List<string> warnings, int requests, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Pakistan import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("Scope: companies listed on the Pakistan Stock Exchange with a recent annual report whose statements can be read as text. " +
                      "Sources: the exchange's company list and profiles (dps.psx.com.pk), each company's annual report PDF, GeoNames. " +
                      "The portal's financial tables are licensed third-party data and are not used.").AppendLine();
        sb.AppendLine($"- Listed companies checked: {listed}");
        sb.AppendLine($"- Companies included: **{companies}**");
        sb.AppendLine($"- Years of figures: {periods}");
        sb.AppendLine($"- Chief executives' pay: {pay}");
        sb.AppendLine($"- Postcodes written: {codes}");
        sb.AppendLine($"- Network requests this run (cached reports aren't downloaded again): {requests}").AppendLine();
        sb.AppendLine("| Area | Companies |").AppendLine("|---|---|");
        foreach (var g in included.GroupBy(i => i.Region).OrderByDescending(g => g.Count())) sb.AppendLine($"| {g.Key} | {g.Count()} |");
        sb.AppendLine().AppendLine($"## Included ({included.Count})").AppendLine();
        sb.AppendLine("| Ticker | Company | Town | Accounts | Latest revenue | Years | CEO pay |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var line in included.Select(i => i.Line).Order(StringComparer.Ordinal)) sb.AppendLine(line);
        sb.AppendLine().AppendLine($"## Excluded ({excluded.Count})").AppendLine();
        foreach (var g in excluded.GroupBy(e => Regex.Replace(e[(e.IndexOf(": ", StringComparison.Ordinal) + 2)..], "\".*\"|FY\\d{4}", "…")).OrderByDescending(g => g.Count()))
            sb.AppendLine($"- {g.Key}: {g.Count()}");
        sb.AppendLine();
        foreach (var line in excluded.Order(StringComparer.Ordinal)) sb.AppendLine($"- {line}");
        sb.AppendLine().AppendLine("## Needs review").AppendLine();
        foreach (var line in warnings) sb.AppendLine($"- {line}");
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }

    [GeneratedRegex(@"\((R|B|GEM)\)|\bCLASS\s+B\b", RegexOptions.IgnoreCase)] private static partial Regex NotAnOrdinaryShare();
    [GeneratedRegex(@"<tr>[\s\S]*?</tr>")] private static partial Regex TableRow();
    [GeneratedRegex(@"<td[^>]*>([\s\S]*?)</td>")] private static partial Regex TableCell();
    [GeneratedRegex(@"/download/document/(?<id>\d+)\.pdf")] private static partial Regex PdfLink();
    [GeneratedRegex(@"annual\s+(report|accounts|audited\s+accounts|financial\s+statements)|audited\s+(annual\s+)?(accounts|financial\s+statements)", RegexOptions.IgnoreCase)] private static partial Regex AnnualReportTitle();
    [GeneratedRegex(@"quarter|half[\s-]*year|interim|nine\s+months|six\s+months|condensed|notice|corrigendum|addendum|errat", RegexOptions.IgnoreCase)] private static partial Regex NotAnnual();
}
