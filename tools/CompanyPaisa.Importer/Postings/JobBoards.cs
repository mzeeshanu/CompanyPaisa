using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Postings;

/// <summary>
/// A company's public job board on a hiring system that publishes its openings: Greenhouse ("doordashusa"), Lever
/// ("palantir"), Ashby ("ramp"), SmartRecruiters ("Visa") or Workday (host "nvidia.wd5.myworkdayjobs.com", board
/// "nvidia/NVIDIAExternalCareerSite").
/// </summary>
public sealed record JobBoard(string System, string Board, string? Host = null)
{
    public const string Greenhouse = "greenhouse", Lever = "lever", Ashby = "ashby", SmartRecruiters = "smartrecruiters", Workday = "workday";

    /// <summary>The board people see, for the report and for links.</summary>
    public string PublicUrl => System switch
    {
        Greenhouse => $"https://job-boards.greenhouse.io/{Board}",
        Lever => $"https://jobs.lever.co/{Board}",
        Ashby => $"https://jobs.ashbyhq.com/{Board}",
        SmartRecruiters => $"https://jobs.smartrecruiters.com/{Board}",
        _ => $"https://{Host}/{Board.Split('/')[^1]}"
    };
}

public static partial class JobBoards
{
    /// <summary>The board a link points at, or null ("https://jobs.lever.co/plaid/abc" → lever/plaid).</summary>
    public static JobBoard? FromUrl(string? url)
    {
        if (!Uri.TryCreate(WebUtility.HtmlDecode(url ?? "").Replace("\\/", "/"), UriKind.Absolute, out var u)) return null;
        var host = u.Host.ToLowerInvariant();
        var segments = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var first = segments.FirstOrDefault();
        if (host.EndsWith("greenhouse.io", StringComparison.Ordinal))
        {
            // boards.greenhouse.io/embed/job_board?for=acme, boards.greenhouse.io/acme/jobs/1, boards-api.greenhouse.io/v1/boards/acme
            var token = Query(u, "for") ?? (first is "v1" && segments.Length > 2 ? segments[2] : first is "embed" ? null : first);
            return Valid(token) ? new JobBoard(JobBoard.Greenhouse, token!.ToLowerInvariant()) : null;
        }
        if (host is "jobs.lever.co" or "jobs.eu.lever.co" || host == "api.lever.co")
        {
            var token = host == "api.lever.co" && segments.Length > 2 ? segments[2] : first;
            return Valid(token) ? new JobBoard(JobBoard.Lever, token!.ToLowerInvariant()) : null;
        }
        if (host is "jobs.ashbyhq.com") return Valid(first) ? new JobBoard(JobBoard.Ashby, first!) : null;
        if (host is "jobs.smartrecruiters.com" or "careers.smartrecruiters.com")
            return Valid(first) && first is not ("oneclick-ui" or "sr-jobs") ? new JobBoard(JobBoard.SmartRecruiters, first!) : null;
        if (WorkdayHost().Match(host) is { Success: true } wd)
        {
            // tenant.wd5.myworkdayjobs.com/en-US/External/job/... or /wday/cxs/tenant/External/jobs
            var parts = segments.SkipWhile(s => Locale().IsMatch(s)).ToList();
            var site = parts.FirstOrDefault() is "wday" && parts.Count > 3 ? parts[3] : parts.FirstOrDefault();
            if (!Valid(site) || site is "job" or "details" or "login" or "wday") return null;
            return new JobBoard(JobBoard.Workday, $"{wd.Groups["tenant"].Value}/{site}", host);
        }
        return null;
    }

    /// <summary>Every board a page links to or embeds (links, iframes and script tags, including escaped JSON).</summary>
    public static List<JobBoard> FindInHtml(string html) =>
        BoardUrl().Matches(html.Replace("\\/", "/")).Select(m => FromUrl(m.Value)).OfType<JobBoard>().Distinct().ToList();

    /// <summary>Links on a careers page that lead to the list of openings ("Search jobs", "View all openings").</summary>
    public static IEnumerable<Uri> JobListLinks(string html, Uri page)
    {
        foreach (Match m in Anchor().Matches(html))
        {
            var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(m.Groups["text"].Value, "<[^>]+>", " ")), @"\s+", " ").Trim();
            if (text.Length > 50 || !OpeningsWords().IsMatch(text)) continue;
            if (Uri.TryCreate(page, WebUtility.HtmlDecode(m.Groups["href"].Value), out var u) && u.Scheme is "http" or "https") yield return u;
        }
    }

    private static bool Valid(string? token) => token is { Length: >= 2 and <= 80 } && TokenChars().IsMatch(token);

    private static string? Query(Uri u, string key) =>
        u.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0] == key && p.Length == 2)?[1];

    [GeneratedRegex(@"^(?<tenant>[a-z0-9-]+)\.wd\d+\.myworkdayjobs\.com$")] private static partial Regex WorkdayHost();
    [GeneratedRegex(@"^[a-z]{2}(?:-[A-Za-z]{2})?$")] private static partial Regex Locale();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")] private static partial Regex TokenChars();
    [GeneratedRegex(@"https?://(?:[a-z0-9-]+\.)*(?:greenhouse\.io|lever\.co|ashbyhq\.com|smartrecruiters\.com|myworkdayjobs\.com)[^\s""'<>\\)]*", RegexOptions.IgnoreCase)]
    private static partial Regex BoardUrl();
    [GeneratedRegex(@"<a\b[^>]*?\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>[\s\S]{0,300}?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();
    [GeneratedRegex(@"\b(?:search|view|see|browse|explore|find)\b.{0,20}\b(?:jobs|openings|positions|opportunities|roles|careers)\b|\b(?:open|current|job) (?:positions|openings|roles)\b|^jobs$|^openings$", RegexOptions.IgnoreCase)]
    private static partial Regex OpeningsWords();
}
