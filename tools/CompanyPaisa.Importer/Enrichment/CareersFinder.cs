using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>
/// Finds a company's careers page the way a visitor would: open its home page and follow the "Careers" / "Jobs" /
/// "Join us" link (including links to job boards such as Workday, Greenhouse or Lever). If the home page has no such
/// link, tries /careers and /jobs. Honours the site's robots.txt, sends a plain identifying User-Agent, reads at most
/// a few pages per company, and never submits anything.
/// </summary>
public sealed partial class CareersFinder(HttpClient http)
{
    public const string UserAgent = "CompanyPaisa/1.0 (+https://companypaisa.com)";

    public async Task<string?> FindAsync(string website, CancellationToken ct)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var home)) return null;
        var robots = await RobotsAsync(home, ct);
        if (!robots.Allows(home.AbsolutePath)) return null;

        var (page, finalUri) = await GetAsync(home, ct);
        if (page is not null && finalUri is not null)
        {
            var best = Links(page, finalUri).Select(l => (l.Url, Score: Score(l.Text, l.Url))).Where(l => l.Score > 0)
                .OrderByDescending(l => l.Score).ThenBy(l => l.Url.AbsoluteUri.Length).FirstOrDefault();
            if (best.Url is not null) return best.Url.AbsoluteUri;
        }

        // No link on the home page: the usual addresses, kept only if they really are a careers page.
        var root = finalUri ?? home;
        foreach (var path in new[] { "/careers", "/jobs" })
        {
            if (!robots.Allows(path)) continue;
            var candidate = new Uri(root, path);
            var (text, landed) = await GetAsync(candidate, ct);
            if (text is null || landed is null) continue;
            if (landed.AbsolutePath.Trim('/').Length == 0) continue;   // redirected back to the home page
            if (CareersWords().IsMatch(TitleOf(text))) return landed.AbsoluteUri;
        }
        return null;
    }

    /// <summary>How likely a link is the careers page: its text first, then its address; 0 = not one.</summary>
    internal static int Score(string text, Uri url)
    {
        var t = WebUtility.HtmlDecode(TagPattern().Replace(text, " ")).Trim();
        if (t.Length > 60 || NotCareers().IsMatch(t) || NotCareers().IsMatch(url.AbsolutePath)) return 0;
        var score = 0;
        if (ExactCareers().IsMatch(t)) score += 10;
        else if (CareersWords().IsMatch(t)) score += 6;
        if (JobBoards().IsMatch(url.Host)) score += 5;
        if (CareersPath().IsMatch(url.AbsolutePath) || CareersHost().IsMatch(url.Host)) score += 4;
        return score >= 6 ? score : 0;
    }

    private static IEnumerable<(string Text, Uri Url)> Links(string html, Uri baseUri)
    {
        foreach (Match m in AnchorPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value.Trim());
            if (href.StartsWith('#') || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(baseUri, href, out var url) || url.Scheme is not ("http" or "https")) continue;
            var text = m.Groups["text"].Value;
            // An icon link: its label is in title or aria-label.
            if (TagPattern().Replace(text, "").Trim().Length == 0) text = m.Groups["attrs"].Value;
            yield return (text, url);
        }
    }

    private async Task<(string? Html, Uri? Final)> GetAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (null, null);
            if (response.Content.Headers.ContentType?.MediaType is { } type && !type.Contains("html", StringComparison.OrdinalIgnoreCase)) return (null, null);
            // At most 1.5 MB: a home page, not a download.
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[1_500_000];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            return (System.Text.Encoding.UTF8.GetString(buffer, 0, read), response.RequestMessage?.RequestUri ?? url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    private async Task<Robots> RobotsAsync(Uri home, CancellationToken ct)
    {
        var (text, _) = await GetAsyncPlain(new Uri(home, "/robots.txt"), ct);
        return Robots.Parse(text ?? "", "CompanyPaisa");
    }

    private async Task<(string? Text, Uri? Final)> GetAsyncPlain(Uri url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await http.SendAsync(request, ct);
            // Bytes, not ReadAsStringAsync: a charset .NET doesn't know ("ISO-8859-2") would throw.
            return response.IsSuccessStatusCode ? (System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(ct)), url) : (null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    private static string TitleOf(string html) => TitlePattern().Match(html) is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value) : "";

    [GeneratedRegex(@"<a\b(?<attrs>[^>]*?)\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>[\s\S]{0,400}?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorPattern();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagPattern();
    [GeneratedRegex(@"<title[^>]*>([\s\S]{0,300}?)</title>", RegexOptions.IgnoreCase)] private static partial Regex TitlePattern();
    [GeneratedRegex(@"^\s*(careers?|jobs|job openings|open positions|careers at .{1,40}|work with us|join us|join our team)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ExactCareers();
    [GeneratedRegex(@"\b(careers?|jobs?|join (us|our team)|work (with|for) us|vacanc(y|ies)|job openings|we'?re hiring)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CareersWords();
    [GeneratedRegex(@"/(careers?|jobs|join-?us|work-?with-?us|vacancies|opportunities)(/|$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex CareersPath();
    [GeneratedRegex(@"^(careers?|jobs)\.", RegexOptions.IgnoreCase)] private static partial Regex CareersHost();
    [GeneratedRegex(@"(myworkdayjobs|greenhouse\.io|lever\.co|smartrecruiters|icims|taleo|successfactors|jobvite|workable|ashbyhq|recruitee|bamboohr|teamtailor|rippling-ats|oraclecloud\.com)", RegexOptions.IgnoreCase)]
    private static partial Regex JobBoards();
    /// <summary>Links that mention jobs but aren't the careers page (a news story, a job scam warning, a "jobs report").</summary>
    [GeneratedRegex(@"scam|fraud|news|press|blog|story|article|report|linkedin|twitter|facebook|investor", RegexOptions.IgnoreCase)]
    private static partial Regex NotCareers();
}

/// <summary>The rules in a robots.txt that apply to us (our own group, else "*"): Disallow / Allow prefixes, longest wins.</summary>
public sealed class Robots
{
    private readonly List<(string Path, bool Allow)> _rules = [];

    public static Robots Parse(string text, string agent)
    {
        var groups = new List<(List<string> Agents, List<(string, bool)> Rules)>();
        (List<string> Agents, List<(string, bool)> Rules)? current = null;
        var lastWasAgent = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (key == "user-agent")
            {
                if (current is null || !lastWasAgent) { current = ([], []); groups.Add(current.Value); }
                current.Value.Agents.Add(value.ToLowerInvariant());
                lastWasAgent = true;
                continue;
            }
            lastWasAgent = false;
            if (current is null || key is not ("allow" or "disallow") || value.Length == 0) continue;
            current.Value.Rules.Add((value, key == "allow"));
        }
        var mine = groups.FirstOrDefault(g => g.Agents.Any(a => a != "*" && agent.Contains(a, StringComparison.OrdinalIgnoreCase)));
        var chosen = mine.Agents is not null ? mine : groups.FirstOrDefault(g => g.Agents.Contains("*"));
        var robots = new Robots();
        if (chosen.Rules is not null) robots._rules.AddRange(chosen.Rules);
        return robots;
    }

    public bool Allows(string path)
    {
        var p = string.IsNullOrEmpty(path) ? "/" : path;
        var match = _rules.Where(r => p.StartsWith(r.Path.TrimEnd('*').Replace("$", ""), StringComparison.Ordinal))
            .OrderByDescending(r => r.Path.Length).FirstOrDefault();
        return match.Path is null || match.Allow;
    }
}
