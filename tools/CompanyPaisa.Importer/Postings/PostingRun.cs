using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Enrichment;
using CompanyPaisa.Importer.Salaries;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Postings;

/// <summary>Settings for --postings (appsettings section "Importer:Postings"). Paths are relative to the repo root.</summary>
public sealed class PostingOptions
{
    /// <summary>Each company's job board (or that none was found), reviewable and editable.</summary>
    public string BoardsPath { get; set; } = "data/reference/job-boards.csv";
    /// <summary>Every ad seen, kept on this computer between runs (not published).</summary>
    public string StorePath { get; set; } = "data/cache/postings/postings.db";
    public string ReportPath { get; set; } = "data/import-report-postings.md";
    /// <summary>Look for a board again after this many days when none was found.</summary>
    [Range(1, 3650)] public int RecheckDays { get; set; } = 90;
    /// <summary>Companies worked on at once (each hiring system's host still gets one request at a time).</summary>
    [Range(1, 64)] public int Parallelism { get; set; } = 16;
    /// <summary>New Workday / SmartRecruiters ads read per board per run (the rest wait for the next run).</summary>
    [Range(10, 100_000)] public int MaxNewDetailsPerBoard { get; set; } = 800;
    /// <summary>Ads seen in the last this-many months make the salaries.</summary>
    [Range(1, 60)] public int MonthsKept { get; set; } = 12;
}

/// <summary>
/// --postings [find] [fetch]: salaries by job title from the pay ranges US companies put in their job ads (required by
/// pay-transparency laws in California, Colorado, New York, Washington, Illinois and more). (1) find: each US company's
/// job board on Greenhouse, Lever, Ashby, SmartRecruiters or Workday — from its careers link, its careers page (robots.txt
/// honoured) or, for the first three, its short name when the board's name matches. (2) fetch: the US ads on each board,
/// their pay range read from the ad, remembered between runs; then summarised by job title and city into the database.
/// </summary>
public sealed class PostingRun(IOptions<ImporterOptions> options, RepoPaths paths, ILoggerFactory loggers)
{
    private readonly PostingOptions _o = options.Value.Postings;
    private readonly ILogger _log = loggers.CreateLogger<PostingRun>();

    public async Task<int> RunAsync(IReadOnlyCollection<string> steps, CancellationToken ct)
    {
        bool Step(string s) => steps.Count == 0 || steps.Contains(s, StringComparer.OrdinalIgnoreCase);
        var dbPath = paths.Resolve(options.Value.Output.DatabasePath);
        SqliteDataStore.Upgrade(dbPath);
        var data = SqliteDataStore.Read(dbPath, DateTimeOffset.UtcNow);
        var companies = data.Companies.Where(c => EnrichmentRun.MarketOf(c) == "sec").ToList();
        var boards = ReadBoards();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true, MaxAutomaticRedirections = 6,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = TimeSpan.FromSeconds(20) };
        var reader = new BoardReader(http, _log, _o.MaxNewDetailsPerBoard);

        if (Step("find")) await FindAsync(http, reader, companies, boards, today, ct);

        using var store = new PostingStore(paths.Resolve(_o.StorePath));
        var fetched = new ConcurrentDictionary<string, (int Ads, int WithPay)>(StringComparer.OrdinalIgnoreCase);
        if (Step("fetch"))
        {
            var todo = boards.Values.Where(b => b.Board is not null && companies.Any(c => c.CompanyId.Equals(b.CompanyId, StringComparison.OrdinalIgnoreCase))).ToList();
            var done = 0;
            var storeGate = new Lock();
            await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = _o.Parallelism, CancellationToken = ct }, async (row, token) =>
            {
                var board = row.Board!;
                Dictionary<string, Posting> known;
                lock (storeGate) known = store.Known(board);
                List<Posting> open;
                try { open = await reader.ReadAsync(board, id => known.GetValueOrDefault(id), token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning("{Company}: {Board} failed ({Message})", row.CompanyId, board.PublicUrl, ex.Message); return; }
                lock (storeGate) store.Save(board, row.CompanyId, open, today);
                fetched[row.CompanyId] = (open.Count, open.Count(p => p.Pay is not null));
                var n = Interlocked.Increment(ref done);
                if (n % 25 == 0) _log.LogInformation("Job ads: {Done}/{Total} boards read", n, todo.Count);
            });
        }

        // Salaries by job title from the ads of the last months, into the database.
        var seen = store.WithPay(today.AddMonths(-_o.MonthsKept));
        var places = UsPlaces.Load(paths.Resolve(options.Value.Geo.ZipTableOutput));
        var ids = companies.Select(c => c.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = Summarise(seen.Where(s => ids.Contains(s.CompanyId)).ToList(), places);
        if (seen.Count > 0)
        {
            var source = new JobSalarySource(seen.Min(s => s.FirstSeen), seen.Max(s => s.LastSeen),
                "Pay ranges in the companies' US job ads (Greenhouse, Lever, Ashby, SmartRecruiters, Workday)", JobSalary.JobAds);
            SqliteDataStore.ReplaceJobSalaries(dbPath, rows, source);
        }
        await WriteReportAsync(companies, boards, fetched, store.Counts(), rows, ct);
        _log.LogInformation("Job ads: boards for {Boards} companies; {Titles:N0} job titles with pay at {Companies:N0} companies ({Places:N0} city rows)",
            boards.Values.Count(b => b.Board is not null), rows.Count(r => r.City is null), rows.Select(r => r.CompanyId).Distinct().Count(), rows.Count(r => r.City is not null));
        return 0;
    }

    // ---- Finding boards --------------------------------------------------------------------------------------------

    private async Task FindAsync(HttpClient http, BoardReader reader, List<Company> companies, Dictionary<string, BoardRow> boards, DateOnly today, CancellationToken ct)
    {
        var todo = companies.Where(c => !boards.TryGetValue(c.CompanyId, out var b) || b.Board is null && b.CheckedOn < today.AddDays(-_o.RecheckDays)).ToList();
        _log.LogInformation("Looking for the job boards of {Count} companies", todo.Count);
        var done = 0;
        var gate = new Lock();
        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = _o.Parallelism, CancellationToken = ct }, async (c, token) =>
        {
            (JobBoard? Board, string How) found = (null, "");
            try { found = await FindBoardAsync(http, reader, c, token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug("{Company}: {Message}", c.CompanyId, ex.Message); }
            lock (gate)
            {
                boards[c.CompanyId] = new BoardRow(c.CompanyId, found.Board, found.How, today);
                if (++done % 100 == 0)
                {
                    WriteBoards(boards);
                    _log.LogInformation("Job boards: {Done}/{Total} companies looked at, {Found} boards so far", done, todo.Count, boards.Values.Count(b => b.Board is not null));
                }
            }
        });
        // One board guessed from a name for two companies ("alliance") belongs to at most one of them: drop the guesses.
        foreach (var shared in boards.Values.Where(b => b.Board is not null).GroupBy(b => b.Board).Where(g => g.Count() > 1).ToList())
            foreach (var guess in shared.Where(b => b.FoundBy == "name"))
                boards[guess.CompanyId] = guess with { Board = null, FoundBy = "" };
        WriteBoards(boards);
    }

    private static async Task<(JobBoard?, string)> FindBoardAsync(HttpClient http, BoardReader reader, Company c, CancellationToken ct)
    {
        // 1. The careers link is the board itself.
        if (JobBoards.FromUrl(c.CareersUrl) is { } direct) return (direct, "careers link");

        // 2. The careers page links to or embeds it, or one of its "search jobs" links does.
        if (Uri.TryCreate(c.CareersUrl, UriKind.Absolute, out var careers))
        {
            var (html, landed) = await PageAsync(http, careers, ct);
            if (landed is not null && JobBoards.FromUrl(landed.AbsoluteUri) is { } redirected) return (redirected, "careers page");
            if (html is not null)
            {
                if (Pick(JobBoards.FindInHtml(html)) is { } onPage) return (onPage, "careers page");
                foreach (var link in JobBoards.JobListLinks(html, landed ?? careers).Distinct().Take(2))
                {
                    if (JobBoards.FromUrl(link.AbsoluteUri) is { } linked) return (linked, "careers page");
                    var (next, nextLanded) = await PageAsync(http, link, ct);
                    if (nextLanded is not null && JobBoards.FromUrl(nextLanded.AbsoluteUri) is { } followed) return (followed, "careers page");
                    if (next is not null && Pick(JobBoards.FindInHtml(next)) is { } deeper) return (deeper, "careers page");
                }
            }
        }

        // 3. The company's short name as a Greenhouse, Lever or Ashby board, kept only when it's clearly theirs: Greenhouse's
        //    board is named like the company (a shorter name only under the company's own website name); Lever and Ashby
        //    don't say whose a board is, so only the website's name, and only when the ads name the company.
        var domain = DomainLabel(c.Website);
        foreach (var slug in Slugs(c))
        {
            var gh = new JobBoard(JobBoard.Greenhouse, slug);
            if (await reader.ProbeAsync(gh, ct) is (true, { } name, _) && (SameName(name, c.Name) || slug == domain && SameCompany(name, c.Name)))
                return (gh, "name");
            if (slug != domain) continue;
            foreach (var other in new[] { new JobBoard(JobBoard.Lever, slug), new JobBoard(JobBoard.Ashby, slug) })
                if (await reader.ProbeAsync(other, ct) is (true, _, var text) && Mentions(text, c.Name)) return (other, "name");
        }
        return (null, "");
    }

    /// <summary>The same name once legal words are dropped ("DoorDash USA" and "DoorDash, Inc.").</summary>
    internal static bool SameName(string boardName, string companyName)
    {
        var a = EmployerMatcher.NameKey(boardName);
        var b = EmployerMatcher.NameKey(companyName);
        return a.Length >= 3 && (a == b || a.Replace(" ", "") == b.Replace(" ", ""));
    }

    /// <summary>Ad text that names the company in full ("Analog Devices", not just "analog").</summary>
    internal static bool Mentions(string text, string companyName)
    {
        var name = EmployerMatcher.NameKey(companyName);
        if (name.Length < 4) return false;
        var plain = " " + System.Text.RegularExpressions.Regex.Replace(PayText.Plain(text).ToLowerInvariant(), "[^a-z0-9]+", " ") + " ";
        return plain.Contains(" " + name + " ", StringComparison.Ordinal);
    }

    /// <summary>A page links to several boards (a partner's, an old one): the system it links to most, Workday first on a tie.</summary>
    private static JobBoard? Pick(List<JobBoard> found) =>
        found.GroupBy(b => b).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.System == JobBoard.Workday ? 0 : 1).Select(g => g.Key).FirstOrDefault();

    private static IEnumerable<string> Slugs(Company c)
    {
        var slugs = new List<string>();
        if (DomainLabel(c.Website) is { } label) slugs.Add(label);
        var words = EmployerMatcher.NameKey(c.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0) slugs.Add(string.Concat(words));
        if (words.Length > 1 && words[0].Length >= 4) slugs.Add(words[0]);
        return slugs.Where(s => s.Length >= 3).Distinct();
    }

    private static string? DomainLabel(string? website)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var u)) return null;
        var parts = u.Host.Split('.');
        return parts.Length >= 2 ? parts[^2].ToLowerInvariant() : null;
    }

    /// <summary>
    /// A board named "DoorDash USA" is DoorDash's, and "Robinhood" is Robinhood Markets': the same name, or the board's is
    /// the start of the company's. Not "Apple Leisure Group" for Apple: a longer board name is another business.
    /// </summary>
    internal static bool SameCompany(string boardName, string companyName)
    {
        var a = EmployerMatcher.NameKey(boardName);
        var b = EmployerMatcher.NameKey(companyName);
        if (a.Length < 3 || b.Length < 3) return false;
        return a == b || a.Replace(" ", "") == b.Replace(" ", "") || a.Length >= 4 && b.StartsWith(a + " ", StringComparison.Ordinal);
    }

    /// <summary>A page's HTML (at most 2 MB) and where it ended up, when robots.txt allows it.</summary>
    private static async Task<(string? Html, Uri? Landed)> PageAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        try
        {
            using (var robotsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(url, "/robots.txt")))
            {
                robotsRequest.Headers.TryAddWithoutValidation("User-Agent", CareersFinder.UserAgent);
                using var robotsResponse = await http.SendAsync(robotsRequest, ct);
                var rules = robotsResponse.IsSuccessStatusCode ? Encoding.UTF8.GetString(await robotsResponse.Content.ReadAsByteArrayAsync(ct)) : "";
                if (!Robots.Parse(rules, "CompanyPaisa").Allows(url.AbsolutePath)) return (null, null);
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", CareersFinder.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var landed = response.RequestMessage?.RequestUri ?? url;
            if (!response.IsSuccessStatusCode) return (null, landed);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[2_000_000];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            return (Encoding.UTF8.GetString(buffer, 0, read), landed);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    // ---- Summary ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Per company and job title: the number of ads, the typical bottom, middle and top of the advertised range (medians),
    /// the lowest bottom and highest top, and the newest ad's link; the same per city the ads name.
    /// </summary>
    internal static List<JobSalary> Summarise(IReadOnlyList<SeenPosting> seen, UsPlaces places)
    {
        var rows = new List<JobSalary>();
        foreach (var job in seen.GroupBy(s => (s.CompanyId, Key: JobTitles.Key(s.Posting.Title))).Where(g => g.Key.Key.Length > 0)
                     .OrderBy(g => g.Key.CompanyId, StringComparer.Ordinal).ThenByDescending(g => g.Count()))
        {
            var title = JobTitles.Display(job.Select(s => s.Posting.Title));
            var url = job.OrderByDescending(s => s.LastSeen).First().Posting.Url;
            rows.Add(Stats(job.Key.CompanyId, title, null, null, null, url, job.ToList()));
            foreach (var place in job.SelectMany(s => UsPlaces.Parse(s.Posting.Location).Select(p => (Place: p, Ad: s))).GroupBy(x => x.Place)
                         .OrderByDescending(g => g.Count()))
                rows.Add(Stats(job.Key.CompanyId, title, place.Key.City, place.Key.State, places.Locate(place.Key.City, place.Key.State), null,
                    place.Select(x => x.Ad).ToList()));
        }
        return rows;
    }

    private static JobSalary Stats(string companyId, string title, string? city, string? state, GeoPoint? point, string? url, List<SeenPosting> ads)
    {
        var mins = ads.Select(a => a.Posting.Pay!.Min).Order().ToArray();
        var maxes = ads.Select(a => a.Posting.Pay!.Max).Order().ToArray();
        var middles = ads.Select(a => a.Posting.Pay!.Middle).Order().ToArray();
        var low = SalaryRun.Percentile(mins, 0.5);
        var high = SalaryRun.Percentile(maxes, 0.5);
        var median = Math.Clamp(SalaryRun.Percentile(middles, 0.5), low, high);
        return new JobSalary
        {
            CompanyId = companyId, Source = JobSalary.JobAds, Title = title, City = city, State = state, Point = point, Url = url,
            Filings = ads.Count, Min = mins[0], Low = low, Median = median, High = high, Max = maxes[^1]
        };
    }

    // ---- Boards table ----------------------------------------------------------------------------------------------

    private sealed record BoardRow(string CompanyId, JobBoard? Board, string FoundBy, DateOnly CheckedOn);

    private Dictionary<string, BoardRow> ReadBoards()
    {
        var boards = new Dictionary<string, BoardRow>(StringComparer.OrdinalIgnoreCase);
        // company_id,system,board,host,found_by,checked_on — an empty system: looked, none found.
        foreach (var f in Csv.Read(paths.Resolve(_o.BoardsPath)).Skip(1).Where(f => f.Count >= 6))
        {
            if (!DateOnly.TryParse(f[5], CultureInfo.InvariantCulture, out var checkedOn)) continue;
            var board = f[1].Length > 0 && f[2].Length > 0 ? new JobBoard(f[1], f[2], f[3].Length > 0 ? f[3] : null) : null;
            boards[f[0]] = new BoardRow(f[0], board, f[4], checkedOn);
        }
        return boards;
    }

    private void WriteBoards(Dictionary<string, BoardRow> boards) =>
        Csv.Write(paths.Resolve(_o.BoardsPath), ["company_id", "system", "board", "host", "found_by", "checked_on"],
            boards.Values.OrderBy(b => b.CompanyId, StringComparer.OrdinalIgnoreCase).Select(b => new[]
            {
                b.CompanyId, b.Board?.System ?? "", b.Board?.Board ?? "", b.Board?.Host ?? "", b.FoundBy,
                b.CheckedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }));

    // ---- Report ----------------------------------------------------------------------------------------------------

    private async Task WriteReportAsync(List<Company> companies, Dictionary<string, BoardRow> boards, ConcurrentDictionary<string, (int Ads, int WithPay)> fetched,
        (int All, int WithPay) stored, List<JobSalary> rows, CancellationToken ct)
    {
        var found = boards.Values.Where(b => b.Board is not null).ToList();
        var titles = rows.Where(r => r.City is null).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Job ads import report").AppendLine();
        sb.AppendLine($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `--postings`.").AppendLine();
        sb.AppendLine("| | |").AppendLine("|---|---|");
        sb.AppendLine($"| US-listed companies | {companies.Count:N0} |");
        sb.AppendLine($"| Job boards found | {found.Count:N0} ({string.Join(", ", found.GroupBy(b => b.Board!.System).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count():N0}"))}) |");
        sb.AppendLine($"| Found by | {string.Join(", ", found.GroupBy(b => b.FoundBy).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count():N0}"))} |");
        sb.AppendLine($"| This run: boards read | {fetched.Count:N0}, {fetched.Values.Sum(f => f.Ads):N0} US ads, {fetched.Values.Sum(f => f.WithPay):N0} with a pay range |");
        sb.AppendLine($"| All ads seen so far | {stored.All:N0} ({stored.WithPay:N0} with a pay range) |");
        sb.AppendLine($"| Companies with salaries from ads | {titles.Select(t => t.CompanyId).Distinct().Count():N0} |");
        sb.AppendLine($"| Job titles | {titles.Count:N0} |");
        sb.AppendLine($"| City rows | {rows.Count - titles.Count:N0} |").AppendLine();
        // From every ad with pay in the window (not just this run's reads), so a summary-only run reports the same.
        sb.AppendLine("## Most ads with pay").AppendLine().AppendLine("| Company | Board | Ads with pay | Titles |").AppendLine("|---|---|---:|---:|");
        foreach (var g in titles.GroupBy(t => t.CompanyId).OrderByDescending(g => g.Sum(t => t.Filings)).Take(60))
            sb.AppendLine($"| {g.Key} | {(boards.TryGetValue(g.Key, out var b) && b.Board is not null ? b.Board.PublicUrl : "")} | {g.Sum(t => t.Filings):N0} | {g.Count():N0} |");
        sb.AppendLine().AppendLine("## Boards with US ads but no pay ranges read").AppendLine();
        sb.AppendLine("Either the ads don't state pay (no US pay-transparency state), or they state it in a way the reader misses.").AppendLine();
        foreach (var (id, f) in fetched.Where(kv => kv.Value.Ads >= 10 && kv.Value.WithPay == 0).OrderByDescending(kv => kv.Value.Ads).Take(40))
            sb.AppendLine($"- {id}: {f.Ads:N0} US ads — {boards[id].Board!.PublicUrl}");
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }
}
