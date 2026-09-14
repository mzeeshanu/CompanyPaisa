using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Uk;

/// <summary>One executive director's single total figure for one year, in whole currency units.</summary>
public sealed record UkPayRow(string Name, int Year, decimal Salary, decimal Bonus, decimal LongTerm, decimal Other, decimal Total, bool Verified);

public sealed record UkPayResult(List<UkPayRow> Rows, List<string> Warnings, string Currency);

/// <summary>
/// Reads the "Single total figure of remuneration – Executive Directors" table from a UK annual report (iXBRL XHTML).
/// Reports are either real HTML tables or PDFs converted to positioned text; both are flattened to text lines
/// (a table row or a converted text line = one line; cells separated by tabs) and read from there.
/// Two layouts: directors as column groups (one column per year each) or directors as rows.
/// The stated total is authoritative; rows whose parts don't add up are flagged, with the gap in Other.
/// </summary>
public static partial class RemunerationParser
{
    private sealed record Line(string Label, List<decimal> Values, List<int> Years, string Raw);

    public static UkPayResult Parse(string html)
    {
        var lines = ToLines(html);
        var warnings = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            // A column heading can be split over two lines ("Single total" / "figure").
            if (!IsAnchor(lines[i]) && !(i + 1 < lines.Count && lines[i].Length < 40 && IsAnchor(lines[i] + " " + lines[i + 1]))) continue;
            var window = Stitch(Window(lines, i));
            var (scale, currency) = Units(lines, i, window);
            var parsed = CarryLabels(window.Select(ParseLine).ToList());
            var rows = ByColumns(parsed, scale) ?? ByRows(parsed, scale);
            if (rows is { Count: > 0 })
            {
                var good = rows.Where(r => r.Total is >= 10_000 and <= 500_000_000 && LooksLikeName(r.Name) && TotalIsLargest(r)).ToList();
                if (good.Count == 0) continue;
                if (good.Count < rows.Count) warnings.Add($"skipped {rows.Count - good.Count} row(s) with implausible totals");
                return new UkPayResult(Dedupe(good), warnings, currency);
            }
        }
        warnings.Add("no single total figure table recognised");
        return new UkPayResult([], warnings, "GBP");
    }

    /// <summary>Diagnostics for --debug-uk-pay: the lines around each anchor.</summary>
    public static IEnumerable<string> Describe(string html)
    {
        var lines = ToLines(html);
        for (var i = 0; i < lines.Count; i++)
            if (IsAnchor(lines[i]))
            {
                yield return $"=== anchor at line {i}: {lines[i]}";
                foreach (var l in Window(lines, i)) yield return "  " + l.Replace('\t', '|');
            }
    }

    // ---------- text lines ----------

    internal static List<string> ToLines(string html)
    {
        var s = Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<ix:header[\s\S]*?</ix:header>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<span[^>]*>(?:\s|&#160;|&nbsp;|&#xa0;)+</span>", "\t", RegexOptions.IgnoreCase);   // gap spans = column breaks
        s = Regex.Replace(s, @"</t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<br\s*/?>|</(tr|p|div|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
        return s.Split('\n')
            .Select(l => Regex.Replace(Regex.Replace(l, @"[ \r\f\v]+", " "), @"\s*\t[\s\t]*", "\t").Trim(' ', '\t'))
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static bool IsAnchor(string line) =>
        line.Length < 200 && SingleFigure().IsMatch(line) && !NonExec().IsMatch(line) && !line.Contains("fee", StringComparison.OrdinalIgnoreCase);

    private static List<string> Window(List<string> lines, int anchor)
    {
        var end = Math.Min(lines.Count, anchor + 160);
        for (var j = anchor + 4; j < end; j++)
            if (NonExec().IsMatch(lines[j]) && (SingleFigure().IsMatch(lines[j]) || lines[j].Contains("fees", StringComparison.OrdinalIgnoreCase))) { end = j; break; }
        return lines.GetRange(anchor + 1, end - anchor - 1);
    }

    /// <summary>
    /// Some reports put every table cell on its own line ("Pascal Soriot" / "2025" / "1,545" / "143" …).
    /// When most of the window looks like that, glue each run of number cells onto the text or year cell before it,
    /// starting a new row at each name, label or year that follows numbers.
    /// </summary>
    internal static List<string> Stitch(List<string> window)
    {
        static bool NumberCell(string l) => CellNumber().IsMatch(l);
        var cells = window.Count(NumberCell);
        if (window.Count == 0 || cells < 8 || cells < window.Count * 0.3) return window;

        var result = new List<string>();
        var numbersInRow = 0;
        foreach (var line in window)
        {
            var isYear = YearToken().IsMatch(line);
            if (result.Count > 0 && NumberCell(line) && !(isYear && numbersInRow >= 2))
            {
                result[^1] += "\t" + line;
                if (!isYear) numbersInRow++;
                continue;
            }
            result.Add(line);
            numbersInRow = 0;
        }
        return result;
    }

    private static (decimal Scale, string Currency) Units(List<string> lines, int anchor, List<string> window)
    {
        var text = string.Join(' ', lines.Skip(Math.Max(0, anchor - 3)).Take(6).Concat(window.Take(40)));
        var currency = text.Contains('$') && !text.Contains('£') ? "USD" : text.Contains('€') && !text.Contains('£') ? "EUR" : "GBP";
        if (Thousands().IsMatch(text)) return (1000m, currency);
        if (Millions().IsMatch(text)) return (1_000_000m, currency);
        return (0m, currency);   // unknown: decided from the size of the totals
    }

    private static Line ParseLine(string raw)
    {
        var values = new List<decimal>();
        var years = new List<int>();
        var labelEnd = raw.Length;
        foreach (Match m in Token().Matches(raw))
        {
            var t = m.Value.Trim();
            if (YearToken().Match(t) is { Success: true } y) { years.Add(YearOf(y)); labelEnd = Math.Min(labelEnd, m.Index); continue; }
            if (t is "–" or "-" or "—" or "nil" or "Nil") { values.Add(0); labelEnd = Math.Min(labelEnd, m.Index); continue; }
            var negative = t.StartsWith('(') && t.EndsWith(')') || t.StartsWith('-');
            if (decimal.TryParse(t.Trim('(', ')', '-', '£', '$', '€'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            {
                values.Add(negative ? -v : v);
                labelEnd = Math.Min(labelEnd, m.Index);
            }
        }
        var label = Footnote().Replace(raw[..labelEnd], "").Replace('\t', ' ').Trim(' ', ':', '–', '-');
        return new Line(label, values, years, raw);
    }

    /// <summary>
    /// Converted PDFs sometimes put a row's label and its numbers on separate lines ("PSP" / "(b)" / "5,665 5,227…").
    /// A numbers-only line takes the label of the text-only line just above it.
    /// </summary>
    private static List<Line> CarryLabels(List<Line> lines)
    {
        var result = new List<Line>(lines.Count);
        string? pending = null;
        foreach (var l in lines)
        {
            if (l.Values.Count == 0 && l.Years.Count == 0 && l.Label.Length is > 0 and < 60) { pending = l.Label.Length > 3 || Kind(l.Label) != Component.None ? l.Label : pending; result.Add(l); continue; }
            if (l.Values.Count > 0 && l.Label.Length == 0 && pending is not null) result.Add(l with { Label = pending });
            else result.Add(l);
            if (l.Values.Count > 0) pending = null;
        }
        return result;
    }

    private static int YearOf(Match y)
    {
        if (y.Groups["fy"].Success) return 2000 + int.Parse(y.Groups["fy"].Value, CultureInfo.InvariantCulture);
        var first = int.Parse(y.Groups["y"].Value, CultureInfo.InvariantCulture);
        return y.Groups["y2"].Success ? first + 1 : first;   // "2025/26" is the year ending in 2026
    }

    // ---------- layout 1: directors as column groups ----------

    private static List<UkPayRow>? ByColumns(List<Line> lines, decimal scale)
    {
        var totalIndex = lines.FindIndex(l => Kind(l.Label) == Component.Total && l.Values.Count >= 1);
        if (totalIndex < 0) return null;
        var total = lines[totalIndex];
        var k = total.Values.Count;
        var firstData = lines.FindIndex(l => l.Values.Count == k && Kind(l.Label) != Component.None);
        if (firstData < 0 || firstData > totalIndex) firstData = totalIndex;

        // Column headings only — not the years mentioned in the intro sentence above the table.
        var header = lines.Take(firstData).Where(l => l.Raw.Length < 80).ToList();
        var years = header.SelectMany(l => l.Years).ToList();
        var distinctYears = years.Distinct().Count();
        if (distinctYears == 0) return null;
        var names = HeaderNames(header, k / Math.Max(1, distinctYears));
        if (names.Count == 0 || k != names.Count * distinctYears) return null;

        // Which director / year each column belongs to.
        var cols = new List<(int Director, int Year)>();
        var directorMajor = years.Count == k ? years.Take(distinctYears).Distinct().Count() == distinctYears : true;
        var yearOrder = years.Distinct().ToList();
        for (var j = 0; j < k; j++)
        {
            var director = directorMajor ? j / distinctYears : j % names.Count;
            var year = years.Count == k ? years[j] : yearOrder[directorMajor ? j % distinctYears : j / names.Count];
            cols.Add((director, year));
        }

        scale = scale > 0 ? scale : total.Values.Max() < 100_000 ? 1000m : 1m;
        var parts = lines.Take(totalIndex).Where(l => l.Values.Count == k && Kind(l.Label) is not (Component.None or Component.Total)).ToList();
        return cols.Select((c, j) => Build(names[c.Director], c.Year, total.Values[j] * scale,
            parts.Select(p => (Kind(p.Label), p.Values[j] * scale)))).ToList();
    }

    /// <summary>Director names from the header lines: tab-separated name cells, or a run of words split evenly.</summary>
    private static List<string> HeaderNames(List<Line> header, int expected)
    {
        foreach (var l in header.AsEnumerable().Reverse())
        {
            var cells = l.Raw.Split('\t').Select(c => c.Trim()).Where(c => c.Length > 0 && !YearToken().IsMatch(c)).ToList();
            var named = cells.Where(LooksLikeName).ToList();
            if (named.Count >= 1 && named.Count == cells.Count && named.Count == expected) return named;
            if (cells.Count == 1 && expected > 1)
            {
                var words = cells[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length % expected == 0 && words.Length / expected is >= 2 and <= 4)
                {
                    var size = words.Length / expected;
                    var split = Enumerable.Range(0, expected).Select(n => string.Join(' ', words.Skip(n * size).Take(size))).ToList();
                    if (split.All(LooksLikeName)) return split;
                }
            }
        }
        return [];
    }

    // ---------- layout 2: directors as rows ----------

    private static List<UkPayRow>? ByRows(List<Line> lines, decimal scale)
    {
        // Column titles, e.g. "Salary | Benefits | Pension | Bonus | LTIP | Total".
        var titles = lines.Select(l => l.Raw.Split('\t').Select(c => Kind(Footnote().Replace(c, "").Trim())).ToList())
            .FirstOrDefault(k => k.Count(x => x != Component.None) >= 3 && k.Contains(Component.Total))?
            .Where(x => x != Component.None).ToList();
        var singleYear = lines.SelectMany(l => l.Years).Distinct().Take(2).ToList() is [var only] ? only : (int?)null;

        var result = new List<(string Name, int Year, List<decimal> Values)>();
        string? current = null;
        foreach (var l in lines)
        {
            var name = NameAtStart(l.Label);
            if (name is not null) current = name;
            if (current is null || l.Values.Count < 3) continue;
            var year = l.Years.Count > 0 ? l.Years[0] : singleYear;
            if (year is null) continue;
            // The total is the row's biggest number — usually the last column, but some tables add a "% of total" column after it.
            var totalAt = l.Values.LastIndexOf(l.Values.Max());
            if (totalAt < 2) continue;
            result.Add((current, year.Value, l.Values.Take(totalAt + 1).ToList()));
        }
        if (result.Count == 0) return null;

        scale = scale > 0 ? scale : result.Max(r => r.Values[^1]) < 100_000 ? 1000m : 1m;
        return result.Select(r =>
        {
            var parts = titles is not null && titles.Count == r.Values.Count
                ? titles.Select((t, j) => (t, r.Values[j] * scale)).Where(p => p.t != Component.Total)
                : [(Component.Salary, r.Values[0] * scale)];
            return Build(r.Name, r.Year, r.Values[^1] * scale, parts);
        }).ToList();
    }

    // ---------- shared ----------

    private enum Component { None, Total, Salary, Benefits, Pension, Bonus, LongTerm, Other }

    private static Component Kind(string label)
    {
        var l = label.ToLowerInvariant().Trim();
        if (l.Length == 0) return Component.None;
        if (Regex.IsMatch(l, @"^(total|single (total )?figure)") && !Regex.IsMatch(l, @"fixed|variable|taxable|benefit|pension|salary")) return Component.Total;
        if (Regex.IsMatch(l, @"^total")) return Component.None;   // subtotals: total fixed / variable pay
        if (Regex.IsMatch(l, @"^(base )?salar|^fees|^salary")) return Component.Salary;
        if (Regex.IsMatch(l, @"benefit")) return Component.Benefits;
        if (Regex.IsMatch(l, @"pension|retirement")) return Component.Pension;
        if (Regex.IsMatch(l, @"bonus|annual incentive|short[- ]term|\bstip\b|\bdsbp\b")) return Component.Bonus;
        if (Regex.IsMatch(l, @"ltip|\bpsp\b|\brsp\b|performance share|long[- ]term|restricted share|share award|share plan|incentive plan|buy[- ]?out")) return Component.LongTerm;
        if (Regex.IsMatch(l, @"^other|all[- ]employee|sharesave|\bsip\b|allowance")) return Component.Other;
        return Component.None;
    }

    private static UkPayRow Build(string name, int year, decimal total, IEnumerable<(Component Kind, decimal Value)> parts)
    {
        var list = parts.ToList();
        decimal Sum(params Component[] kinds) => list.Where(p => kinds.Contains(p.Kind)).Sum(p => p.Value);
        var salary = Sum(Component.Salary);
        var bonus = Sum(Component.Bonus);
        var longTerm = Sum(Component.LongTerm);
        var other = Sum(Component.Benefits, Component.Pension, Component.Other);
        var sum = salary + bonus + longTerm + other;
        var verified = total > 0 && Math.Abs(sum - total) <= Math.Max(2_000m, total * 0.01m);
        if (!verified) other = total - salary - bonus - longTerm;   // the stated total wins
        return new UkPayRow(name, year, salary, bonus, longTerm, other, total, verified);
    }

    private static List<UkPayRow> Dedupe(List<UkPayRow> rows) =>
        rows.GroupBy(r => (r.Name.ToUpperInvariant(), r.Year)).Select(g => g.First()).ToList();

    private static readonly HashSet<string> NotNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Total", "Salary", "Salaries", "Benefits", "Pension", "Bonus", "Executive", "Executives", "Director", "Directors", "Chief", "Officer",
        "Fixed", "Variable", "Pay", "Annual", "Remuneration", "Group", "Plc", "Single", "Figure", "Audited", "Table", "Finance", "Financial",
        "The", "Of", "And", "For", "Year", "Former", "Current", "Notes", "Note", "Value", "Share", "Shares", "Awards", "Award", "Long", "Term",
        "Incentive", "Plan", "Other", "Taxable", "Fees", "Non", "Chair", "Chairman", "Operating", "Deputy", "Interim", "Joined", "Appointed", "Stepped", "Down",
        // Words from the performance-target and KPI tables that sit next to the pay table.
        "Target", "Threshold", "Maximum", "Minimum", "Weighting", "Actual", "Outturn", "Vesting", "Payout", "Measure", "Metric", "Performance",
        "Information", "Additional", "Report", "Strategic", "Corporate", "Governance", "Statement", "Accounts", "Satisfaction", "Customer",
        "Customers", "Living", "Wage", "Systems", "Homes", "National", "Growth", "Profit", "Revenue", "Return", "Earnings", "Cash", "Flow",
        "Safety", "Employee", "Employees", "Engagement", "Carbon", "Emissions", "Scope", "Net", "Zero", "Underlying", "Adjusted", "Group's",
        "If", "On", "Target", "Committee", "Policy", "Base", "Percentage", "Change", "Median", "Ratio", "Quartile", "Upper", "Lower", "Page",
        "Estimated", "Deferred", "Advanced", "Corporation", "Tax", "Predictability", "Gender", "One", "Two", "Three", "FY", "Vested", "Granted",
        "Dividend", "Dividends", "Equivalent", "Equivalents", "Price", "Appreciation", "Relating", "Awarded", "Holding", "Period", "Buyout", "Legacy",
        "Complaints", "Resolved", "Colleague", "Colleagues", "Risk", "Capital", "Ratio", "Cost", "Costs", "Income", "Operating", "Strategic", "Objectives"
    };

    /// <summary>"Simon Gibbins’" / "Graham Sutherland’s" → the bare name.</summary>
    public static string CleanName(string name) => Regex.Replace(name.Trim(), @"[’']s?$", "").Trim();

    /// <summary>
    /// UK executive directors' single figures sit between a part-time salary and the biggest packages on record;
    /// outside that range it's a non-executive's fees, a unit mix-up (£ read as £'000) or another table.
    /// </summary>
    public static bool PlausibleExecutivePay(UkPayRow r) =>
        r.Total is >= 150_000m and <= 25_000_000m &&
        // These are real, named people: publish a row only when the table was clearly understood — its parts add up —
        // or it has a believable salary and a total no more than 15× that salary (long-term awards vesting in a good year).
        (r.Verified || (r.Salary is >= 150_000m and <= 2_500_000m && r.Total <= r.Salary * 15));

    private static readonly HashSet<string> NameParticles = new(StringComparer.OrdinalIgnoreCase)
        { "Sir", "Dame", "Dr", "Lord", "Baroness", "de", "van", "von", "da", "del", "di", "le", "la", "du", "bin", "St" };

    /// <summary>
    /// A person's name as printed in a pay table: 2–3 words (4 with a title or particle), each capitalised,
    /// no all-capitals words (headings) and none of the table words around it.
    /// </summary>
    public static bool LooksLikeName(string s)
    {
        var words = s.Trim().TrimEnd(')', '(').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 4 || (words.Length == 4 && !words.Any(NameParticles.Contains))) return false;
        if (words.Distinct(StringComparer.OrdinalIgnoreCase).Count() < words.Length) return false;   // "David X David Y"
        return words.All(w =>
        {
            var bare = w.Trim(',', '.');
            if (NotNameWords.Contains(bare)) return false;
            if (bare.Length >= 3 && bare.All(ch => !char.IsLetter(ch) || char.IsUpper(ch))) return false;   // "STRATEGIC"
            return NameWord().IsMatch(w) || NameParticles.Contains(bare);
        });
    }

    /// <summary>A pay row's stated total is its biggest number (a targets or KPI table's usually isn't).</summary>
    public static bool TotalIsLargest(UkPayRow r) =>
        r.Total > 0 && r.Salary <= r.Total * 1.01m && r.Bonus <= r.Total * 1.01m && r.LongTerm <= r.Total * 1.01m;

    private static string? NameAtStart(string label)
    {
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var n = Math.Min(5, words.Length); n >= 2; n--)
        {
            var candidate = string.Join(' ', words.Take(n));
            // "David Schwimmer Anna Manz": three words straight into another capitalised word is two names, not one.
            if (n == 3 && words.Length > 3 && char.IsUpper(words[3][0]) && NameWord().IsMatch(words[3])) continue;
            if (LooksLikeName(candidate)) return candidate;
        }
        return null;
    }

    [GeneratedRegex(@"single\s+(total\s+)?figure|total\s+single\s+figure", RegexOptions.IgnoreCase)] private static partial Regex SingleFigure();
    [GeneratedRegex(@"non[-\s–]?executive", RegexOptions.IgnoreCase)] private static partial Regex NonExec();
    [GeneratedRegex(@"[£$€]\s?[’'‘]?\s?000|[£$€]k\b|\(000s?\)|thousands", RegexOptions.IgnoreCase)] private static partial Regex Thousands();
    [GeneratedRegex(@"[£$€]\s?m\b|[£$€]\s?million|\bmillions\b", RegexOptions.IgnoreCase)] private static partial Regex Millions();
    [GeneratedRegex(@"(?<![\w.,/])(?:\(?-?[£$€]?\d{1,3}(?:,\d{3})+(?:\.\d+)?\)?|\(?-?[£$€]?\d+(?:\.\d+)?\)?|FY\d{2}|20\d{2}/\d{2,4}|[–—-]|[Nn]il)(?![\w/])")]
    private static partial Regex Token();
    [GeneratedRegex(@"^(?:(?<y>20[0-4]\d)(?:/(?<y2>\d{2}|20\d{2}))?|FY(?<fy>\d{2}))$")] private static partial Regex YearToken();
    [GeneratedRegex(@"^(?:\(?-?[£$€]?\d{1,3}(?:,\d{3})*(?:\.\d+)?\)?|\d+(?:\.\d+)?|20\d{2}/\d{2,4}|FY\d{2}|[–—-]|[Nn]il)$")] private static partial Regex CellNumber();
    [GeneratedRegex(@"\((?:[a-z]{1,2}|[ivx]{1,4}|\d{1,2})\)|(?<=[A-Za-z])\d{1,2}\b|[*†‡]")] private static partial Regex Footnote();
    [GeneratedRegex(@"^(?:Dame|Sir|Dr|Lord|Baroness|[A-Z][a-zA-ZÀ-ÿ'’\-]+|[A-Z]\.)$")] private static partial Regex NameWord();
}
