using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>A table cell placed on a column grid (colspan/rowspan resolved).</summary>
public sealed record GridCell(int Start, int End, string Text)
{
    public bool Overlaps(int start, int end) => Start <= end && End >= start;
}

/// <summary>
/// Lays out an HTML table as rows of cells on a shared column grid, so a value can be matched to its header
/// by position even when headers span several cells ("$", number, footnote) or split over multiple rows.
/// </summary>
public static partial class HtmlTableGrid
{
    public static List<List<GridCell>> Build(string tableHtml)
    {
        var rows = new List<List<GridCell>>();
        var carried = new Dictionary<int, int>();   // column → rows still covered by a rowspan from above

        foreach (Match tr in RowPattern().Matches(tableHtml))
        {
            var row = new List<GridCell>();
            var blocked = carried.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();
            var next = new Dictionary<int, int>();
            foreach (var (col, left) in carried) if (left > 1) next[col] = left - 1;

            var c = 0;
            foreach (Match td in CellPattern().Matches(tr.Value))
            {
                while (blocked.Contains(c)) c++;
                var attrs = td.Groups[1].Value;
                var colspan = Span(attrs, ColspanAttr());
                var rowspan = Span(attrs, RowspanAttr());
                row.Add(new GridCell(c, c + colspan - 1, Clean(td.Groups[2].Value)));
                if (rowspan > 1) for (var k = c; k < c + colspan; k++) next[k] = rowspan - 1;
                c += colspan;
            }
            carried = next;
            if (row.Any(x => x.Text.Length > 0)) rows.Add(row);
        }
        return rows;
    }

    private static int Span(string attrs, Regex pattern)
    {
        var m = pattern.Match(attrs);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is > 0 and < 50 ? n : 1;
    }

    public static string Clean(string cellHtml)
    {
        var s = TagPattern().Replace(cellHtml, " ");
        s = WebUtility.HtmlDecode(s)
            .Replace(' ', ' ').Replace("​", "").Replace("﻿", "").Replace("‌", "").Replace("‍", "")
            .Replace('—', '—').Replace('–', '–').Replace('’', '\'');
        return WhitespacePattern().Replace(s, " ").Trim();
    }

    [GeneratedRegex(@"<tr\b[\s\S]*?</tr>", RegexOptions.IgnoreCase)] private static partial Regex RowPattern();
    [GeneratedRegex(@"<t[dh]\b([^>]*)>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase)] private static partial Regex CellPattern();
    [GeneratedRegex(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)] private static partial Regex ColspanAttr();
    [GeneratedRegex(@"rowspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)] private static partial Regex RowspanAttr();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagPattern();
    [GeneratedRegex(@"\s+")] private static partial Regex WhitespacePattern();
}
