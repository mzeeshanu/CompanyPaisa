using System.Globalization;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>The pay ratio a proxy statement discloses: its median employee's pay, the CEO's, and "N to 1".</summary>
public sealed record PayRatio(decimal MedianEmployeePay, decimal CeoPay, decimal Ratio);

/// <summary>
/// Reads the CEO pay ratio disclosure that US companies put in their proxy statement (Item 402(u) of Regulation S-K):
/// "the median of the annual total compensation of all employees was $68,254; the CEO's was $63,209,845; the ratio is
/// 926 to 1". The words vary a lot, so the rule is arithmetic, not phrasing: a stated ratio is kept only when two dollar
/// amounts close to it divide to that ratio. The smaller one is the median employee, the larger the CEO. Nothing is
/// kept when the numbers don't agree.
/// </summary>
public static partial class PayRatioReader
{
    /// <param name="html">The proxy statement.</param>
    /// <param name="ceoTotals">
    /// Totals from the proxy's summary compensation table for the ratio's year: many disclosures state the median and the
    /// ratio but point at the table for the CEO's figure.
    /// </param>
    public static PayRatio? Read(string html, IReadOnlyCollection<decimal>? ceoTotals = null)
    {
        var text = NewHireParser.PlainText(html);
        // The section can be named anywhere (a table of contents, a summary); try each mention until one adds up.
        var anchors = Anchor().Matches(text).ToList();
        foreach (var anchor in anchors)
            if (FromWindow(Window(text, anchor.Index)) is { } found && MatchesTable(found, ceoTotals)) return found;
        if (ceoTotals is not { Count: > 0 }) return null;
        foreach (var anchor in anchors)
            if (FromWindow(Window(text, anchor.Index), ceoTotals) is { } found) return found;
        return null;
    }

    /// <summary>
    /// The CEO figure should be near a total in the proxy's own pay table for the year (within 3×: a CEO who joined
    /// mid-year is annualised). Catches a pair of unrelated amounts that happen to divide to a stated number.
    /// </summary>
    private static bool MatchesTable(PayRatio found, IReadOnlyCollection<decimal>? totals) =>
        totals is not { Count: > 0 } || totals.Any(t => t > 0 && found.CeoPay >= t / 3 && found.CeoPay <= t * 3);

    private static string Window(string text, int at) => text[Math.Max(0, at - 1_500)..Math.Min(text.Length, at + 5_000)];

    /// <summary>The ratio, median and CEO amounts in one stretch of text (or the CEO's from <paramref name="ceoTotals"/>), when they agree.</summary>
    internal static PayRatio? FromWindow(string window, IReadOnlyCollection<decimal>? ceoTotals = null) =>
        // "N to 1" first; a bare number after "ratio" only when no stated ratio adds up.
        FromWindow(window, Ratios(window, RatioPattern(), ReversedRatioPattern()).ToList(), ceoTotals, stated: true) ??
        FromWindow(window, Ratios(window, BareRatioPattern()).ToList(), ceoTotals, stated: false);

    /// <param name="stated">The ratio was written as a ratio ("N to 1"), not a bare number near the word "ratio".</param>
    private static PayRatio? FromWindow(string window, List<decimal> ratios, IReadOnlyCollection<decimal>? ceoTotals, bool stated)
    {
        if (ratios.Count == 0) return null;
        // A median under $10,000 happens (retailers with mostly part-time staff, workforces abroad), but only a ratio the
        // company wrote as a ratio is trusted to back it.
        var lowestMedian = stated ? 1_000m : 10_000m;
        var amounts = Amounts(window).ToList();
        var medianAt = MedianWord().Matches(window).Select(m => m.Index).ToList();
        var ceoCandidates = ceoTotals is null
            ? amounts
            : ceoTotals.Select(t => (Value: t, Index: -1, Dollar: true)).ToList();

        PayRatio? best = null;
        var bestDistance = int.MaxValue;
        foreach (var ratio in ratios)
            foreach (var median in amounts.Where(a => a.Dollar && a.Value >= lowestMedian && a.Value <= 1_500_000))
                foreach (var ceo in ceoCandidates.Where(a => a.Value > median.Value && a.Value >= 50_000))
                {
                    if (!Agrees(ceo.Value / median.Value, ratio)) continue;
                    // Several pairs can agree (a table repeats the figures): prefer the median amount nearest the word "median".
                    var distance = medianAt.Count == 0 ? int.MaxValue - 1 : medianAt.Min(i => Math.Abs(i - median.Index));
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = new PayRatio(median.Value, ceo.Value, ratio);
                }
        return best;
    }

    /// <summary>A stated ratio is rounded ("approximately 256 to 1"): allow the rounding, and 2% for the rest.</summary>
    internal static bool Agrees(decimal computed, decimal stated) =>
        Math.Abs(computed - stated) <= Math.Max(1m, stated * 0.02m);

    private static IEnumerable<decimal> Ratios(string window, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
            foreach (Match m in pattern.Matches(window))
            {
                var digits = m.Groups["n"].Value;
                // A year ("for 2025") isn't a ratio.
                if (Regex.IsMatch(digits, @"^(19|20)\d\d$")) continue;
                // Under 2 isn't a CEO pay ratio worth showing, and "1 to 1" turns up in other contexts (matching, voting).
                if (decimal.TryParse(digits.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n is >= 2 and <= 20_000)
                    yield return n;
            }
    }

    /// <summary>Dollar amounts, and big bare numbers ("8,965,642" in a table whose $ sits in another cell) as possible CEO pay.</summary>
    private static IEnumerable<(decimal Value, int Index, bool Dollar)> Amounts(string window)
    {
        foreach (Match m in AmountPattern().Matches(window))
        {
            if (!decimal.TryParse(m.Groups["n"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) continue;
            if (m.Groups["million"].Success) n *= 1_000_000m;
            var dollar = m.Groups["dollar"].Success;
            if (!dollar && n < 1_000_000m) continue;
            yield return (Math.Round(n, 0), m.Index, dollar);
        }
    }

    [GeneratedRegex(@"pay\s+ratio|ratio\s+of\s+(?:the\s+)?(?:annual\s+)?total\s+compensation|median\s+(?:of\s+the\s+)?(?:annual\s+total\s+compensation|employee)", RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();

    // "926 to 1", "926:1", "926-to-1", "926 times", "926 to one"
    [GeneratedRegex(@"(?<![\d,.$])(?<n>\d{1,3}(?:,\d{3})*(?:\.\d+)?)\s*(?:(?:-?\s*to\s*-?\s*|:\s*)(?:1|one)(?![\d,])|\s+times\b)", RegexOptions.IgnoreCase)]
    private static partial Regex RatioPattern();

    // "1:44.7", "1 to 44.7"
    [GeneratedRegex(@"(?<![\d,.$])1\s*(?::|to)\s*(?<n>\d{1,3}(?:,\d{3})*(?:\.\d+)?)(?![\d,]|\.\d)", RegexOptions.IgnoreCase)]
    private static partial Regex ReversedRatioPattern();

    // A table row: "Ratio of … annual total compensation … 95.3" (no "to 1").
    // Not a count ("13,200 U.S. employees"), a percentage or a page number reference.
    [GeneratedRegex(@"\bratio\b[^$]{0,250}?(?<![\d,.$])(?<n>\d{1,3}(?:,\d{3})*(?:\.\d+)?)(?![\d,%]|\.\d|\s*(?:u\.?s\.?|non-|full|part|employees|people|workers|individuals|countries|percent|%|months|years|days))", RegexOptions.IgnoreCase)]
    private static partial Regex BareRatioPattern();

    // "$68,254", "$ 63,209,845", "$1.2 million", and bare "8,965,642"
    [GeneratedRegex(@"(?:(?<dollar>\$)\s*)?(?<![\d,.])(?<n>\d{1,3}(?:,\d{3})+(?:\.\d+)?|(?<=\$\s*)\d+(?:\.\d+)?)(?<million>\s*million)?", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"median", RegexOptions.IgnoreCase)]
    private static partial Regex MedianWord();
}
