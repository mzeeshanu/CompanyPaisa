using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Enrichment;

namespace CompanyPaisa.Importer.Anz;

/// <summary>An annual report (or full-year results) PDF linked from a company's own website.</summary>
/// <param name="Year">The financial year it covers, as its link names it ("2025 Annual Report", "FY25").</param>
public sealed record ReportLink(string Url, string Text, int Year, int Score);

/// <summary>
/// Finds a company's latest annual reports on its own website, the way an investor would: the home page → the
/// "Investors" / "Reports" pages → the PDF links named "Annual Report", "Appendix 4E" or "Full year results". Stays on the
/// company's own site (and the PDF hosts it links to), honours robots.txt, identifies itself and reads a handful of pages
/// per company. Links to the ASX's or NZX's own websites are never followed: their terms don't allow automated access.
/// </summary>
public sealed partial class ReportFinder(HttpClient http, int maxPages = 25, Action<string>? trace = null)
{
    public const string UserAgent = CareersFinder.UserAgent;

    /// <summary>The site's report links, best first (newest year, then the most likely to be the full report).</summary>
    public async Task<List<ReportLink>> FindAsync(string website, CancellationToken ct)
    {
        if (!Uri.TryCreate(website, UriKind.Absolute, out var home)) return [];
        var robots = new Dictionary<string, Robots>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new PriorityQueue<(Uri Url, int Depth), int>();
        queue.Enqueue((home, 0), 0);
        var reports = new Dictionary<string, ReportLink>(StringComparer.OrdinalIgnoreCase);
        var bare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // found only as an address, without link words
        string? site = null;   // the company's registrable domain, after any redirect from the address Wikidata gives
        var pages = 0;

        while (queue.TryDequeue(out var next, out _) && pages < maxPages)
        {
            var (url, depth) = next;
            if (!seen.Add(Canonical(url))) continue;
            if (!await AllowedAsync(robots, url, ct)) continue;
            var (html, final) = await GetPageAsync(url, ct);
            pages++;
            trace?.Invoke($"page {url} → {(html is null ? "nothing" : final?.AbsoluteUri)}");
            if (html is null || final is null) continue;
            if (site is null)
            {
                site = Registrable(final.Host);
                // Investor pages often sit at the usual addresses without a link on a shop-front home page.
                foreach (var path in CommonPaths) queue.Enqueue((new Uri(final, path), 1), -5);
            }
            foreach (var (text, link) in Links(html, final))
            {
                if (Blocked().IsMatch(link.Host)) continue;
                if (IsPdf(link))
                {
                    // The same file as a link with words and as a bare address: the words win.
                    var key = Canonical(link);
                    if (ReportScore(text, link) is { } r && (!reports.ContainsKey(key) || bare.Contains(key) && text.Length > 0))
                    {
                        reports[key] = r;
                        if (text.Length == 0) bare.Add(key); else bare.Remove(key);
                    }
                    continue;
                }
                // Pages: only the company's own site (investor centres on a subdomain count), a few levels deep.
                if (depth >= 3 || Registrable(link.Host) != site || seen.Contains(Canonical(link))) continue;
                var score = PageScore(text, link);
                if (score > 0) queue.Enqueue((link, depth + 1), -score + depth * 3);
            }
        }
        return reports.Values.OrderByDescending(r => r.Year).ThenByDescending(r => r.Score).ThenBy(r => r.Url.Length).ToList();
    }

    /// <summary>How likely a page link leads to the reports: investor centres and report archives score highest.</summary>
    internal static int PageScore(string rawText, Uri url)
    {
        var text = Text(rawText);
        if (text.Length > 80) return 0;
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        if (NotReportPage().IsMatch(text) || NotReportPage().IsMatch(path)) return 0;
        var score = 0;
        if (ReportsPage().IsMatch(text)) score += 10;
        else if (InvestorPage().IsMatch(text)) score += 7;
        if (ReportsPage().IsMatch(path)) score += 6;
        else if (InvestorPage().IsMatch(path) || InvestorPage().IsMatch(url.Host)) score += 4;
        return score;
    }

    /// <summary>A PDF link's financial year and how likely it holds the full-year statements; null when it isn't one.</summary>
    internal static ReportLink? ReportScore(string rawText, Uri url)
    {
        var text = Text(rawText);
        // "/wp-content/uploads/2025/07/Annual-Report-2016.pdf": the upload folder's date isn't the report's year.
        var path = UploadFolder().Replace(Uri.UnescapeDataString(url.AbsolutePath), "/").Replace('_', ' ').Replace('-', ' ').Replace('+', ' ');
        // The link's words and the file's own name; folders ("/results-and-presentations/2026/") only for the year.
        var file = Path.GetFileNameWithoutExtension(path);
        var both = $"{text} {file}";
        // "Annual and Sustainability Report" is the annual report; a sustainability report on its own isn't.
        if (NotAnnual().IsMatch(CombinedReport().Replace(both, "annual report"))) return null;
        int score;
        if (AnnualReport().IsMatch(both)) score = 10;
        else if (Appendix4E().IsMatch(both)) score = 8;
        else if (FullYear().IsMatch(both)) score = 6;
        else return null;
        var year = YearOf(text) ?? YearOf(file) ?? YearOf(Path.GetDirectoryName(path) ?? "");
        return year is { } y ? new ReportLink(url.GetLeftPart(UriPartial.Query), text.Length > 0 ? text : file, y, score) : null;
    }

    /// <summary>"2025 Annual Report", "Annual Report 2024-25", "FY25 results", "AR2025" → the year the financial year ends.</summary>
    internal static int? YearOf(string s)
    {
        var now = DateTime.UtcNow.Year;
        if (SplitYear().Match(s) is { Success: true } split)
        {
            var first = int.Parse(split.Groups["a"].Value, CultureInfo.InvariantCulture);
            var second = split.Groups["b"].Value;
            var y = second.Length == 2 ? first / 100 * 100 + int.Parse(second, CultureInfo.InvariantCulture) : int.Parse(second, CultureInfo.InvariantCulture);
            if (y == first + 1 && y <= now + 1) return y;
        }
        if (FyYear().Match(s) is { Success: true } fy)
        {
            var v = int.Parse(fy.Groups["y"].Value, CultureInfo.InvariantCulture);
            var y = v < 100 ? 2000 + v : v;
            if (y >= 2000 && y <= now + 1) return y;
        }
        // "20260827-appendix-4e.pdf": a date stamp.
        if (DateStamp().Match(s) is { Success: true } stamp) return int.Parse(stamp.Groups["y"].Value, CultureInfo.InvariantCulture);
        var years = PlainYear().Matches(s).Select(m => int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture)).Where(y => y >= 2000 && y <= now + 1).ToList();
        return years.Count > 0 ? years.Max() : null;
    }

    private static readonly string[] CommonPaths = ["/investors", "/investor-centre", "/investor-relations", "/shareholders", "/investor"];

    private static bool IsPdf(Uri url) => url.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> AllowedAsync(Dictionary<string, Robots> robots, Uri url, CancellationToken ct)
    {
        var key = url.GetLeftPart(UriPartial.Authority);
        if (!robots.TryGetValue(key, out var r))
        {
            var (text, _) = await GetAsync(new Uri(url, "/robots.txt"), "text/plain", 500_000, ct);
            robots[key] = r = Robots.Parse(text ?? "", "CompanyPaisa");
        }
        return r.Allows(url.AbsolutePath);
    }

    private Task<(string? Html, Uri? Final)> GetPageAsync(Uri url, CancellationToken ct) => GetAsync(url, "text/html", 3_000_000, ct);

    private async Task<(string? Text, Uri? Final)> GetAsync(Uri url, string accept, int maxBytes, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (null, null);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (accept == "text/html" && type.Length > 0 && !type.Contains("html", StringComparison.OrdinalIgnoreCase)) return (null, null);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[maxBytes];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            return (System.Text.Encoding.UTF8.GetString(buffer, 0, read), response.RequestMessage?.RequestUri ?? url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    private static IEnumerable<(string Text, Uri Url)> Links(string html, Uri baseUri)
    {
        foreach (Match m in AnchorPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value.Trim());
            if (href.StartsWith('#') || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(baseUri, href, out var url) || url.Scheme is not ("http" or "https")) continue;
            var text = m.Groups["text"].Value;
            // An icon or card link: its label is in title / aria-label, or only the address says what it is.
            if (Text(text).Length == 0) text = m.Groups["attrs"].Value;
            yield return (text, url);
        }
        // PDFs a script opens (data-href="…Annual Report.pdf") or listed in the page's data ("\/content\/…pdf"): judged by
        // the file's name alone.
        foreach (Match m in QuotedPdf().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups["u"].Value.Replace("\\/", "/").Trim());
            if (Uri.TryCreate(baseUri, href, out var url) && url.Scheme is "http" or "https") yield return ("", url);
        }
    }

    private static string Text(string html) =>
        WebUtility.HtmlDecode(TagPattern().Replace(html, " ")).Replace(' ', ' ').Trim() is var t ? Spaces().Replace(t, " ") : "";

    private static string Canonical(Uri url) => url.GetLeftPart(UriPartial.Query).TrimEnd('/');

    /// <summary>"investors.qantas.com" → "qantas.com"; "www.fletcherbuilding.co.nz" → "fletcherbuilding.co.nz".</summary>
    internal static string Registrable(string host)
    {
        var parts = host.ToLowerInvariant().Split('.');
        if (parts.Length < 2) return host;
        var twoLevel = parts.Length >= 3 && parts[^2] is "co" or "com" or "org" or "net" or "gov" or "ac" or "asn" or "id" && parts[^1].Length == 2;
        return string.Join('.', parts[^(twoLevel ? 3 : 2)..]);
    }

    [GeneratedRegex(@"<a\b(?<attrs>[^>]*?)\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>[\s\S]{0,600}?)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorPattern();
    [GeneratedRegex(@"[""'](?<u>[^""'<>\s][^""'<>]{0,300}?\.pdf)(\?[^""'<>]*)?[""']", RegexOptions.IgnoreCase)] private static partial Regex QuotedPdf();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagPattern();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    /// <summary>The exchanges' own websites (their terms forbid automated access) and social sites.</summary>
    [GeneratedRegex(@"(^|\.)(asx\.com\.au|nzx\.com|markitdigital\.com|linkedin\.com|facebook\.com|twitter\.com|x\.com|youtube\.com|instagram\.com)$", RegexOptions.IgnoreCase)]
    private static partial Regex Blocked();
    [GeneratedRegex(@"annual\s*(and\s+sustainability\s+|&\s*sustainability\s+|financial\s+|integrated\s+)?report|\bAR\s?20\d{2}\b|annual\s+review|financial\s+statements|financial\s+report", RegexOptions.IgnoreCase)]
    private static partial Regex AnnualReport();
    [GeneratedRegex(@"appendix\s*4\s*e|\b4e\b|preliminary\s+final", RegexOptions.IgnoreCase)] private static partial Regex Appendix4E();
    [GeneratedRegex(@"full[\s-]*year\s+(results?|report|financial)|\bFY\s?\d{2,4}\s+(results?|report)|annual\s+results", RegexOptions.IgnoreCase)] private static partial Regex FullYear();
    /// <summary>Not the full-year accounts: half-years, quarterlies, summaries, and the other documents an annual report sits beside.</summary>
    [GeneratedRegex(@"half[\s-]*year|\bhy\s?\d|\b1h\s?(fy)?\d|\bh1\b|interim|4\s*d\b|quarter|\bq[1-4]\b|sustainab|esg|climate|modern\s+slavery|tax\s+(transparency|contribution)|governance|notice\s+of|proxy|presentation|investor\s+day|webcast|transcript|remuneration\s+report|summary|shareholder\s+review|letter|chair(man)?'?s|addendum|errat|correction|supplement|glossary|appendix\s*4g|4g\b|diversity|reconciliation|policy|charter|prospectus|scheme\s+booklet", RegexOptions.IgnoreCase)]
    private static partial Regex NotAnnual();
    [GeneratedRegex(@"\b(reports?|results|annual[\s-]*reports?|financial[\s-]*(reports?|results|information|statements)|reporting\s+(centre|center|suite)|publications|asx[\s-]*announcements|announcements)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportsPage();
    [GeneratedRegex(@"\b(investors?|shareholders?|investor[\s-]*(centre|center|relations)|ir)\b", RegexOptions.IgnoreCase)] private static partial Regex InvestorPage();
    [GeneratedRegex(@"career|jobs|login|sign[\s-]*in|contact|privacy|terms|cookie|news(room)?\b|media\s+release|blog|shop|store|cart|product|recipe|event|search|subscribe|podcast|vide?o", RegexOptions.IgnoreCase)]
    private static partial Regex NotReportPage();
    [GeneratedRegex(@"annual\s+(and|&)\s+sustainability\s+report", RegexOptions.IgnoreCase)] private static partial Regex CombinedReport();
    [GeneratedRegex(@"/(uploads|files|media|sites/default/files)/20\d{2}/\d{1,2}/", RegexOptions.IgnoreCase)] private static partial Regex UploadFolder();
    [GeneratedRegex(@"\b(?<a>20\d{2})\s*[-/–]\s*(?<b>20\d{2}|\d{2})\b")] private static partial Regex SplitYear();
    [GeneratedRegex(@"\bFY\s?(?<y>20\d{2}|\d{2})\b", RegexOptions.IgnoreCase)] private static partial Regex FyYear();
    [GeneratedRegex(@"(?<!\d)(?<y>20\d{2})(?!\d)")] private static partial Regex PlainYear();
    [GeneratedRegex(@"(?<!\d)(?<y>20\d{2})[01]\d[0-3]\d(?!\d)")] private static partial Regex DateStamp();
}
