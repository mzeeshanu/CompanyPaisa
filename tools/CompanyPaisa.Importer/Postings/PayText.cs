using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Postings;

/// <summary>A pay range stated in a job ad, as yearly US dollars.</summary>
public sealed record PayRange(decimal Min, decimal Max)
{
    public decimal Middle => Math.Round((Min + Max) / 2, 0);
}

/// <summary>
/// Reads the pay range US pay-transparency laws require in job ads: "$120,000 - $160,000", "$45.00/hr – $60.00/hr",
/// "136,000 USD - 218,500 USD", "$211.4K – $290.6K", "between $95K and $120K". Hourly and monthly rates become yearly
/// (2,080 hours). The first plausible range in the ad is kept; sign-on bonuses, revenue figures and other dollar
/// amounts don't pass the checks (a yearly range needs at least $15,000 and at most 4× between its ends).
/// </summary>
public static partial class PayText
{
    public static PayRange? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = Plain(text);
        foreach (Match m in RangePattern().Matches(plain))
        {
            if (!new[] { "d1", "d2", "u1", "u2", "u3", "u4" }.Any(g => m.Groups[g].Success)) continue;   // needs $ or USD
            if (Amount(m.Groups["a"].Value, m.Groups["ak"].Success) is not { } a || Amount(m.Groups["b"].Value, m.Groups["bk"].Success) is not { } b) continue;
            var after = plain.Substring(m.Index + m.Length, Math.Min(80, plain.Length - m.Index - m.Length));
            if (BigUnit().IsMatch(after)) continue;   // "$2 - $3 billion"
            var before = plain.Substring(Math.Max(0, m.Index - 150), Math.Min(150, m.Index));
            // The period is usually stated in or right after the range ("$45 - $60 per hour"), else just before it
            // ("Hourly pay: $45 - $60").
            var per = Period(m.Value + " " + after[..Math.Min(40, after.Length)]);
            if (per == Per.Unknown) per = Period(before.Length > 80 ? before[^80..] : before, nearestLast: true);
            if (Yearly(a, b, per) is { } range) return range;
        }
        return null;
    }

    private enum Per { Unknown, Hour, Month, Year }

    private static Per Period(string context, bool nearestLast = false)
    {
        var hour = HourWords().Match(context);
        var year = YearWords().Match(context);
        var month = MonthWords().Match(context);
        var found = new[] { (hour, Per.Hour), (year, Per.Year), (month, Per.Month) }.Where(x => x.Item1.Success);
        var first = (nearestLast ? found.OrderByDescending(x => x.Item1.Index) : found.OrderBy(x => x.Item1.Index)).FirstOrDefault();
        return first.Item1 is { Success: true } ? first.Item2 : Per.Unknown;
    }

    private static PayRange? Yearly(decimal a, decimal b, Per per)
    {
        var (min, max) = a <= b ? (a, b) : (b, a);
        if (per == Per.Unknown) per = max < 300 ? Per.Hour : max >= 15_000 ? Per.Year : Per.Unknown;
        (min, max) = per switch
        {
            Per.Hour when max is >= 7 and <= 500 => (min * 2080, max * 2080),
            Per.Month when max is >= 1_500 and <= 150_000 => (min * 12, max * 12),
            Per.Year => (min, max),
            _ => (0, 0)
        };
        if (min < 15_000 || max > 3_000_000 || max > min * 4) return null;
        return new PayRange(Math.Round(min, 0), Math.Round(max, 0));
    }

    private static decimal? Amount(string digits, bool thousands)
    {
        if (!decimal.TryParse(digits.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return null;
        return thousands ? v * 1000 : v;
    }

    /// <summary>Job ads come as HTML, sometimes HTML-escaped HTML (Greenhouse): tags out, entities decoded, spaces tidied.</summary>
    internal static string Plain(string text)
    {
        var t = text;
        for (var i = 0; i < 2 && (t.Contains("&lt;") || t.Contains("&amp;")); i++) t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, "<[^>]+>", " ");
        t = WebUtility.HtmlDecode(t).Replace(' ', ' ').Replace(' ', ' ');
        return Regex.Replace(t, @"\s+", " ");
    }

    // "$120,000 - $160,000", "$45.00/hr to $60.00/hr", "136,000 USD - 218,500 USD", "$211.4K – $290.6K", "USD 95,000 and 120,000"
    [GeneratedRegex(@"(?<u1>USD\s?)?(?<d1>\$)?\s?(?<a>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)\s?(?<ak>[kK]\b)?\s?(?:(?<u3>USD)|/\s?(?:hr|hour|yr|year))?\s*(?:-|–|—|to|and)\s*(?<u2>USD\s?)?(?<d2>\$)?\s?(?<b>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)\s?(?<bk>[kK]\b)?(?:\s?(?<u4>USD))?", RegexOptions.IgnoreCase)]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"^\s*(?:million|billion|mm\b|m\b|b\b|bn\b)", RegexOptions.IgnoreCase)] private static partial Regex BigUnit();
    [GeneratedRegex(@"\b(?:per hour|an hour|hourly|/\s?hr\b|/\s?hour\b|hour\b)", RegexOptions.IgnoreCase)] private static partial Regex HourWords();
    [GeneratedRegex(@"\b(?:per year|a year|annual(?:ly|ized)?|yearly|/\s?yr\b|/\s?year\b|salary)", RegexOptions.IgnoreCase)] private static partial Regex YearWords();
    [GeneratedRegex(@"\b(?:per month|a month|monthly|/\s?mo\b)", RegexOptions.IgnoreCase)] private static partial Regex MonthWords();
}
