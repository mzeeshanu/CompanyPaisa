using System.Globalization;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>One row of a proxy statement's Summary Compensation Table.</summary>
public sealed record CompRow(string Name, string Title, int Year, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, bool ComponentsVerified);

public interface ICompensationParser
{
    /// <summary>Finds the Summary Compensation Table in a DEF 14A (HTML) and returns its rows.</summary>
    CompParseResult Parse(string html);
}

public sealed record CompParseResult(IReadOnlyList<CompRow> Rows, IReadOnlyList<string> Warnings);

/// <summary>
/// Best-effort parser for the SEC-mandated Summary Compensation Table (Reg S-K Item 402(c)).
/// The table is laid out on a column grid (<see cref="HtmlTableGrid"/>), headers are merged across rows,
/// and each amount is taken from the cells under its header. Names and titles come from the cells left of
/// the Year column and may span several rows. Rows whose components don't add up to the total are kept but flagged.
/// </summary>
public sealed partial class SummaryCompensationTableParser : ICompensationParser
{
    private enum Col { Name, Year, Salary, Bonus, Stock, StockSubtotal, Option, NonEquity, Pension, Other, Total, Ignore }

    private sealed record Span(int Start, int End, Col Kind);

    public CompParseResult Parse(string html)
    {
        CompParseResult? best = null;
        foreach (Match t in TablePattern().Matches(html))
        {
            // Cheap pre-check before building the grid.
            if (!t.Value.Contains("alary", StringComparison.OrdinalIgnoreCase) || !t.Value.Contains("otal", StringComparison.OrdinalIgnoreCase)) continue;
            var result = ParseTable(HtmlTableGrid.Build(t.Value));
            if (result is not null && (best is null || result.Rows.Count > best.Rows.Count)) best = result;
        }
        return best ?? new CompParseResult([], ["No Summary Compensation Table found."]);
    }

    /// <summary>Diagnostics: how each candidate table's header was understood (used by --debug-proxy).</summary>
    public static IEnumerable<string> Describe(string html)
    {
        foreach (Match t in TablePattern().Matches(html))
        {
            if (!t.Value.Contains("alary", StringComparison.OrdinalIgnoreCase) || !t.Value.Contains("otal", StringComparison.OrdinalIgnoreCase)) continue;
            var grid = HtmlTableGrid.Build(t.Value);
            var firstData = grid.FindIndex(IsDataRow);
            yield return $"--- table: {grid.Count} rows, first data row {firstData}";
            foreach (var row in grid.Take(Math.Max(0, firstData) + 2))
                yield return "  " + string.Join(" | ", row.Select(c => $"[{c.Start}-{c.End}] {c.Text}"));
        }
    }

    private static CompParseResult? ParseTable(List<List<GridCell>> grid)
    {
        var firstData = grid.FindIndex(IsDataRow);
        if (firstData <= 0) return null;
        // Header rows are the ones with header words; anything after them (e.g. a name on its own line) is data.
        var lastHeader = grid.Take(firstData).ToList().FindLastIndex(r => r.Any(c => HeaderWord().IsMatch(c.Text)));
        if (lastHeader < 0) return null;
        var dataStart = lastHeader + 1;

        // Merge header text per grid column (headers are often split over 2-3 rows).
        var header = grid.Take(dataStart).TakeLast(4).Where(r => r.Any(c => HeaderWord().IsMatch(c.Text))).ToList();
        var width = grid.Max(r => r.Count == 0 ? 0 : r.Max(c => c.End)) + 1;
        var colText = Enumerable.Range(0, width)
            .Select(i => string.Join(" ", header.SelectMany(r => r.Where(c => c.Overlaps(i, i) && c.Text.Length > 0).Select(c => c.Text)).Distinct()))
            .ToList();
        var all = string.Join(" ", colText);
        if (!SalaryWord().IsMatch(all) || !TotalWord().IsMatch(all) || !NameWord().IsMatch(all)) return null;

        var spans = new List<Span>();
        for (var i = 0; i < width;)
        {
            var j = i;
            while (j + 1 < width && colText[j + 1] == colText[i]) j++;
            if (colText[i].Length > 0) spans.Add(new Span(i, j, Classify(colText[i])));
            i = j + 1;
        }
        var year = spans.FirstOrDefault(s => s.Kind == Col.Year);
        if (year is null || !spans.Any(s => s.Kind == Col.Total)) return null;
        var amounts = spans.Where(s => s.Start > year.End && s.Kind is not (Col.Ignore or Col.Name or Col.Year)).ToList();
        // Stock awards split into sub-columns with their own subtotal: use the subtotal only.
        if (amounts.Any(s => s.Kind == Col.StockSubtotal))
            amounts = amounts.Where(s => s.Kind != Col.Stock).Select(s => s.Kind == Col.StockSubtotal ? s with { Kind = Col.Stock } : s).ToList();
        if (!amounts.Any(s => s.Kind == Col.Total)) return null;
        // Some filers show two totals (e.g. with / without cancelled awards): the first is the reported one.
        var firstTotal = amounts.First(s => s.Kind == Col.Total);
        amounts = amounts.Where(s => s.Kind != Col.Total || s == firstTotal).ToList();

        return ParseRows(grid.Skip(dataStart).ToList(), year, amounts);
    }

    private static CompParseResult ParseRows(List<List<GridCell>> rows, Span year, List<Span> amounts)
    {
        var warnings = new List<string>();
        var output = new List<CompRow>();
        string? name = null;
        var title = "";
        var justStarted = false;
        var personRows = new List<(int Year, Dictionary<Col, decimal> Values)>();
        var lastYear = int.MaxValue;

        void Flush()
        {
            if (name is not null)
                foreach (var (y, values) in personRows)
                {
                    var row = ToRow(name, CleanTitle(title), y, values, warnings);
                    if (row is not null) output.Add(row);
                }
            name = null; title = ""; personRows.Clear(); justStarted = false;
        }

        foreach (var row in rows)
        {
            var yearCell = row.FirstOrDefault(c => c.Overlaps(year.Start, year.End) && YearCell().IsMatch(c.Text))
                           ?? row.FirstOrDefault(c => YearCell().IsMatch(c.Text));
            var textCells = yearCell is null ? row : row.Where(c => c.End < yearCell.Start);
            var text = string.Join(" ", textCells.Select(c => c.Text).Where(s => s.Length > 0 && !Footnote().IsMatch(s) && ParseAmount(s) is null && s != "$"));

            if (yearCell is null)
            {
                if (text.Length == 0) continue;
                // A name on its own line starts a new person; anything else continues the current title.
                var (candidate, rest) = SplitNameAndTitle(text);
                if (IsPlausibleName(candidate) && (name is null || personRows.Count > 0))
                {
                    var claimsPending = name is null && personRows.Count > 0;   // name printed below its first row
                    if (name is not null) Flush();
                    name = candidate; title = rest; justStarted = !claimsPending;
                }
                else if (name is not null) title += " " + text;
                continue;
            }

            var y = int.Parse(YearCell().Match(yearCell.Text).Groups[1].Value, CultureInfo.InvariantCulture);
            var values = ReadAmounts(row, yearCell, amounts);

            // Some filers put the name on the 2nd row of a person's block: a year row with no text that isn't
            // older than the previous one starts a nameless block that the next name will claim.
            if (text.Length == 0 && name is not null && y >= lastYear && !justStarted) Flush();

            var startsNewPerson = text.Length > 0 && !justStarted && (name is null || y >= lastYear);
            if (startsNewPerson)
            {
                var (candidate, rest) = SplitNameAndTitle(text);
                if (IsPlausibleName(candidate))
                {
                    if (name is not null) Flush();
                    name = candidate; title = rest;      // keeps any nameless rows collected just before
                }
                else if (name is not null) title += " " + text;
            }
            else if (text.Length > 0) title += " " + text;

            if (values.Values.Any(v => v != 0)) personRows.Add((y, values));
            justStarted = false;
            lastYear = y;
        }
        Flush();
        return new CompParseResult(output, warnings);
    }

    /// <summary>
    /// Two ways to read a row's amounts: by grid position under each header, or in order (ignoring "$",
    /// footnotes and blanks) when the row has extra cells the header doesn't. Prefer whichever adds up to the total.
    /// </summary>
    private static Dictionary<Col, decimal> ReadAmounts(List<GridCell> row, GridCell yearCell, List<Span> amounts)
    {
        var byPosition = new Dictionary<Col, decimal>();
        foreach (var span in amounts)
        {
            var v = row.Where(c => c.Overlaps(span.Start, span.End)).Select(c => ParseAmount(c.Text)).FirstOrDefault(a => a is not null) ?? 0;
            byPosition[span.Kind] = byPosition.GetValueOrDefault(span.Kind) + v;
        }
        if (AddsUp(byPosition)) return byPosition;

        var sequence = row.Where(c => c.Start > yearCell.End)
            .Select(c => c.Text.Trim())
            .Where(s => s.Length > 0 && s != "$" && !Footnote().IsMatch(s))
            .Select(ParseAmount).Where(a => a is not null).Select(a => a!.Value).ToList();
        if (sequence.Count == amounts.Count)
        {
            var inOrder = new Dictionary<Col, decimal>();
            for (var i = 0; i < amounts.Count; i++) inOrder[amounts[i].Kind] = inOrder.GetValueOrDefault(amounts[i].Kind) + sequence[i];
            if (AddsUp(inOrder) || byPosition.GetValueOrDefault(Col.Total) <= 0) return inOrder;
        }
        if (byPosition.GetValueOrDefault(Col.Total) <= 0 && sequence.Count > 0)
            byPosition[Col.Total] = sequence[^1];   // at least keep the total (always the last column)
        return byPosition;
    }

    private static bool AddsUp(Dictionary<Col, decimal> v)
    {
        var total = v.GetValueOrDefault(Col.Total);
        var sum = v.Where(kv => kv.Key != Col.Total).Sum(kv => kv.Value);
        return total > 0 && Math.Abs(sum - total) <= Math.Max(2, total * 0.01m);
    }

    private static CompRow? ToRow(string name, string title, int year, Dictionary<Col, decimal> v, List<string> warnings)
    {
        var total = v.GetValueOrDefault(Col.Total);
        if (total <= 0) return null;
        var salary = v.GetValueOrDefault(Col.Salary);
        var bonus = v.GetValueOrDefault(Col.Bonus);
        var stock = v.GetValueOrDefault(Col.Stock) + v.GetValueOrDefault(Col.Option);
        var other = v.GetValueOrDefault(Col.NonEquity) + v.GetValueOrDefault(Col.Pension) + v.GetValueOrDefault(Col.Other);
        var sum = salary + bonus + stock + other;
        var verified = Math.Abs(sum - total) <= Math.Max(2, total * 0.01m);
        if (!verified)
        {
            warnings.Add($"{name} {year}: components {sum:N0} ≠ total {total:N0}; difference shown as Other.");
            // The Total column is authoritative. Keep the pieces we trust and make the rest add up to it.
            if (sum > total) { stock = Math.Max(0, Math.Min(stock, total - salary - bonus)); if (salary + bonus > total) { salary = Math.Min(salary, total); bonus = 0; stock = 0; } }
            other = Math.Max(0, total - salary - bonus - stock);
        }
        return new CompRow(name, title, year, salary, bonus, stock, other, total, verified);
    }

    internal static (string Name, string Title) SplitNameAndTitle(string raw)
    {
        var text = FootnoteInline().Replace(raw, " ");
        text = TrailingNumber().Replace(HonorificPrefix().Replace(Spaces().Replace(text, " ").Trim(), ""), "").Trim();
        var comma = text.IndexOf(',');
        if (comma > 0)
        {
            var candidate = text[..comma].Trim();
            var rest = text[(comma + 1)..].Trim();
            var suffix = SuffixStart().Match(rest);
            if (suffix.Success) { candidate += ", " + suffix.Groups[1].Value; rest = rest[suffix.Length..].TrimStart(',', ' '); }
            var inName = TitleKeyword().Match(candidate);
            if (!inName.Success) return (TrailingNumber().Replace(candidate, "").Trim(), rest);
        }
        var m = TitleKeyword().Match(text);
        if (!m.Success || m.Index == 0) return (text, "");
        var at = m.Index;
        if (at >= 3 && text.Substring(at - 3, 3).Equals("Co-", StringComparison.OrdinalIgnoreCase)) at -= 3;   // "Co-Founder"
        return at > 0 ? (text[..at].Trim(' ', ',', '-', '–', ';'), text[at..].Trim()) : (text, "");
    }

    private static string CleanTitle(string title)
    {
        var t = FootnoteInline().Replace(title, " ");
        t = DefinedTermSuffix().Replace(t, "");                        // `(“PEO”)`, `("CEO")` and cut-off `(“`
        t = Spaces().Replace(t, " ").Trim(' ', ',', '-', ';', '–', '(');
        if (!t.Any(char.IsLower)) t = Geo.Text.TitleCase(t)              // "CHAIR OF THE BOARD AND CEO" → "Chair Of The Board And CEO"
            .Replace(" Of ", " of ").Replace(" And ", " and ").Replace(" The ", " the ");
        return t.Length == 0 ? "Named Executive Officer" : t;
    }

    internal static bool IsPlausibleName(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 6 && name.Length <= 60 && char.IsLetter(name[0]) && char.IsUpper(name[0]) &&
               !TitleKeyword().IsMatch(name) && !name.Contains("Total", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("Year", StringComparison.OrdinalIgnoreCase) && !name.Any(char.IsDigit);
    }

    private static bool IsDataRow(List<GridCell> row)
    {
        var y = row.FindIndex(c => YearCell().IsMatch(c.Text));
        return y >= 0 && row.Skip(y + 1).Any(c => ParseAmount(c.Text) is > 1000);
    }

    private static Col Classify(string header)
    {
        var h = header.ToLowerInvariant();
        if (YearHeader().IsMatch(h.Trim()) || h.EndsWith(" year", StringComparison.Ordinal) && !h.Contains("salary")) return Col.Year;
        if (h.Contains("name") || h.Contains("principal position")) return Col.Name;
        if (h.StartsWith("total", StringComparison.Ordinal)) return Col.Total;   // "Total ($)", "Total With All Cancelled Stock Awards"
        var isStock = h.Contains("stock") || h.Contains("equity award") || h.Contains("rsu") || h.Contains("restricted") || h.Contains("psu");
        var isTotal = h.Contains(" total");
        if (isStock && !h.Contains("non-equity") && !h.Contains("nonequity")) return isTotal ? Col.StockSubtotal : Col.Stock;
        if (isTotal) return h.Contains("salary") ? Col.Salary : Col.Total;
        if (h.Contains("salary")) return Col.Salary;
        if (h.Contains("non-equity") || h.Contains("nonequity") || h.Contains("non equity") || h.Contains("incentive plan")) return Col.NonEquity;
        if (h.Contains("option")) return Col.Option;
        if (h.Contains("bonus")) return Col.Bonus;
        if (h.Contains("pension") || h.Contains("deferred")) return Col.Pension;
        if (h.Contains("other")) return Col.Other;
        return Col.Ignore;
    }

    private static decimal? ParseAmount(string cell)
    {
        if (Footnote().IsMatch(cell.Trim())) return null;
        // "16,200,061 (d)" / "1,000,000(1)" / "250,000*" — drop footnote markers glued to the number.
        var c = FootnoteInline().Replace(LetterFootnote().Replace(cell, ""), "");
        c = c.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        if (c.Length == 0) return null;
        if (c is "-" or "—" or "–" or "--" or "0") return 0;
        var negative = c.StartsWith('(') && c.EndsWith(')');
        c = c.Trim('(', ')');
        return decimal.TryParse(c, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (negative ? -v : v) : null;
    }

    [GeneratedRegex(@"<table[\s\S]*?</table>", RegexOptions.IgnoreCase)] private static partial Regex TablePattern();
    [GeneratedRegex(@"salary", RegexOptions.IgnoreCase)] private static partial Regex SalaryWord();
    [GeneratedRegex(@"\btotal", RegexOptions.IgnoreCase)] private static partial Regex TotalWord();
    [GeneratedRegex(@"name|principal position", RegexOptions.IgnoreCase)] private static partial Regex NameWord();
    [GeneratedRegex(@"^(fiscal\s+)?year\b", RegexOptions.IgnoreCase)] private static partial Regex YearHeader();
    [GeneratedRegex(@"^(?:FY\s*|Fiscal\s+(?:Year\s+)?)?((?:19|20)\d{2})(?:\s*\(\s*\d{1,2}\s*\))*\s*\*?$", RegexOptions.IgnoreCase)] private static partial Regex YearCell();
    [GeneratedRegex(@"^\(\s*(?:\d{1,2}|[a-z])\s*\)$|^\*+$", RegexOptions.IgnoreCase)] private static partial Regex Footnote();
    [GeneratedRegex(@"\(\s*\d{1,2}\s*\)|\*")] private static partial Regex FootnoteInline();
    [GeneratedRegex(@"\s+\d{1,2}$")] private static partial Regex TrailingNumber();
    [GeneratedRegex(@"\(\s*[a-z]{1,2}\s*\)", RegexOptions.IgnoreCase)] private static partial Regex LetterFootnote();
    [GeneratedRegex(@"\(\s*[“""][^)]*\)?\s*$")] private static partial Regex DefinedTermSuffix();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"^(?:Mr|Mrs|Ms|Dr)\.?\s+", RegexOptions.IgnoreCase)] private static partial Regex HonorificPrefix();
    [GeneratedRegex(@"^(Jr\.|Sr\.|Jr|Sr|II|III|IV)(?=[\s,]|$)", RegexOptions.IgnoreCase)] private static partial Regex SuffixStart();
    [GeneratedRegex(@"salary|bonus|stock|option|total|\byear\b|name|position|\(\s*\$\s*\)|compensation", RegexOptions.IgnoreCase)] private static partial Regex HeaderWord();
    [GeneratedRegex(@"\b(Chief|President|Officer|Vice|Executive|Former|Chairman|Chair|CEO|CFO|COO|CTO|General Counsel|Secretary|Treasurer|Director|Senior|EVP|SVP|Interim|Founder|Head|Managing|Principal|Corporate|Group)\b", RegexOptions.IgnoreCase)] private static partial Regex TitleKeyword();
}
