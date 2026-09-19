using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Pk;

/// <summary>
/// The company profile on the exchange's data portal (dps.psx.com.pk/company/{symbol}): what the company files with the
/// exchange about itself — business description, key people, registered address, website, fiscal year end, shares.
/// The page's price and financial tables (licensed third-party data) are not read.
/// </summary>
public sealed record PsxProfile(string? Description, string? Ceo, string? Chair, string? Address, string? Website,
    string? FiscalYearEnd, decimal? MarketCapThousands, long? Shares)
{
    public static PsxProfile Parse(string html)
    {
        string? Item(string head) =>
            Regex.Match(html, $@"<div class=""item__head"">\s*{Regex.Escape(head)}\s*</div>\s*<p>(?<v>[\s\S]*?)</p>", RegexOptions.IgnoreCase) is { Success: true } m
                ? Text(m.Groups["v"].Value) : null;

        var people = Regex.Matches(html, @"<tr><td><strong>(?<name>[^<]+)</strong></td><td>(?<role>[^<]+)</td></tr>")
            .Select(m => (Name: Text(m.Groups["name"].Value), Role: Text(m.Groups["role"].Value))).ToList();
        string? Person(string pattern) => people.FirstOrDefault(p => Regex.IsMatch(p.Role, pattern, RegexOptions.IgnoreCase)).Name;

        decimal? Stat(string label) =>
            Regex.Match(html, $@"<div class=""stats_label"">{label}[\s\S]*?</div>\s*<div class=""stats_value"">(?<v>[^<]+)</div>", RegexOptions.IgnoreCase) is { Success: true } m &&
            decimal.TryParse(m.Groups["v"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

        var website = Regex.Match(html, @"WEBSITE</div>\s*<p>\s*<a href=""(?<u>[^""]+)""", RegexOptions.IgnoreCase) is { Success: true } w ? w.Groups["u"].Value.Trim() : null;
        return new PsxProfile(
            Blank(Item("BUSINESS DESCRIPTION")),
            Blank(Person(@"^(CEO|Chief Executive|Managing Director|MD)\b")),
            Blank(Person(@"^Chair")),
            Blank(Item("ADDRESS")),
            Blank(website),
            Blank(Item("Fiscal Year End")),
            Stat("Market Cap"),
            Stat("Shares") is { } s ? (long)s : null);
    }

    /// <summary>"June" → the last day of June as "06-30".</summary>
    public string? FiscalYearEndMonthDay()
    {
        if (FiscalYearEnd is null || !DateTime.TryParseExact(FiscalYearEnd.Trim(), "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)) return null;
        return new DateOnly(2001, month.Month, DateTime.DaysInMonth(2001, month.Month)).ToString("MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Text(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim().TrimEnd(',').Trim();
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) || s is "-" or "N/A" ? null : s;
}
