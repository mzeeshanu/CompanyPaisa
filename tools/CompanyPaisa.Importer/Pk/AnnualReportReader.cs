using System.Globalization;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Pk;

/// <summary>Revenue and profit for the report's year and the year before, from one statement of profit or loss.</summary>
public sealed record PkStatement(bool Consolidated, int FiscalYear, DateOnly? PeriodEnd, decimal Revenue, decimal PriorRevenue,
    decimal NetIncome, decimal PriorNetIncome, decimal? Eps, decimal? PriorEps, string RevenueLine, int Line);

/// <summary>The chief executive's pay for the report's year (the remuneration note), in rupees.</summary>
public sealed record PkCeoPay(int FiscalYear, decimal Total, decimal Salary, decimal Bonus, bool Verified, int Line);

/// <param name="HeadOffice">The address after "Head office" / "Corporate office" in the company information pages, as printed.</param>
public sealed record PkAnnualReport(IReadOnlyList<PkStatement> Statements, PkCeoPay? CeoPay, string? HeadOffice, IReadOnlyList<string> Warnings)
{
    /// <summary>The group's figures when the company has subsidiaries (consolidated), else the company's own.</summary>
    public PkStatement? Main => Statements.FirstOrDefault(s => s.Consolidated) ?? Statements.FirstOrDefault();
}

/// <summary>
/// Reads a Pakistani annual report's text (see <see cref="PdfLines"/>) by rules, the way an analyst would: the statement of
/// profit or loss (a heading, "For the year ended …", a "2026  2025" column header, the unit such as "(Rupees in '000)", then
/// label rows with a note number and two amounts), and the note "Remuneration of chief executive, directors and executives".
/// Amounts are returned in rupees. Anything that doesn't fit the pattern is left out rather than guessed.
/// </summary>
public static partial class AnnualReportReader
{
    public static PkAnnualReport Read(IReadOnlyList<string> lines) => Read(lines.Select(PdfTextLine.Plain).ToList());

    public static PkAnnualReport Read(IReadOnlyList<PdfTextLine> pdfLines)
    {
        var lines = pdfLines.Select(l => l.Text).ToList();
        var warnings = new List<string>();
        var statements = new List<PkStatement>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsStatementHeading(lines, i, out var consolidated, out var headingLines)) continue;
            if (ReadStatement(lines, i + headingLines, consolidated, i) is { } s)
            {
                // The same statement can be repeated (a summary page); keep the first of each kind.
                if (!statements.Any(x => x.Consolidated == s.Consolidated)) statements.Add(s);
            }
        }

        PkCeoPay? pay = null;
        for (var i = 0; i < lines.Count && pay is null; i++)
            if (RemunerationHeading().IsMatch(lines[i]))
                pay = ReadCeoPay(pdfLines, i, warnings);
        var main = statements.FirstOrDefault(s => s.Consolidated) ?? statements.FirstOrDefault();
        if (pay is not null && main is not null && pay.FiscalYear != main.FiscalYear) pay = null;
        return new PkAnnualReport(statements, pay, HeadOffice(lines), warnings);
    }

    /// <summary>
    /// "Head Office  Lahore  E-1, Sehjpal…" or "Corporate Office and Mailing" with the address on the next line: the
    /// address text (the exchange lists the registered office, which can be a factory far from where the company is run).
    /// </summary>
    private static string? HeadOffice(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < Math.Min(lines.Count, 4000); i++)
        {
            var cells = ColumnGap().Split(Clean(lines[i]));
            if (!HeadOfficeLabel().IsMatch(cells[0])) continue;
            var rest = string.Join(", ", cells.Skip(1));
            var next = string.Join(", ", lines.Skip(i + 1).Take(2).Select(Clean));
            return (rest.Length > 0 ? rest + ", " : "") + next;
        }
        return null;
    }

    // ---- Statement of profit or loss ------------------------------------------------------------------------------

    private static bool IsStatementHeading(IReadOnlyList<string> lines, int i, out bool consolidated, out int used)
    {
        consolidated = false; used = 1;
        var one = Clean(lines[i]);
        var two = i + 1 < lines.Count ? $"{one} {Clean(lines[i + 1])}" : one;
        foreach (var (text, n) in new[] { (one, 1), (two, 2) })
        {
            var m = StatementHeading().Match(text);
            if (!m.Success) continue;
            consolidated = m.Groups["kind"].Value.Equals("consolidated", StringComparison.OrdinalIgnoreCase);
            used = n;
            return true;
        }
        return false;
    }

    private static PkStatement? ReadStatement(IReadOnlyList<string> lines, int start, bool consolidated, int headingLine)
    {
        DateOnly? periodEnd = null;
        int? year = null;
        decimal? unit = null;
        var rows = new List<(string Label, List<decimal> Values, int Line)>();
        decimal? loneAmount = null;
        for (var i = start; i < Math.Min(lines.Count, start + 70); i++)
        {
            var line = Clean(lines[i]);
            if (rows.Count == 0)
            {
                // The header: date, the two years, the unit. A header with more years is a multi-year summary, not the statement.
                periodEnd ??= PeriodEnd(line);
                // A translation into US dollars (banks add one for convenience): not the statement.
                if (ForeignCurrency().IsMatch(line) && !RupeeWord().IsMatch(line)) return null;
                // Horizontal / vertical analysis pages repeat the statement with percentages: not the statement.
                if (AnalysisPage().IsMatch(line) || headingLine > 0 && AnalysisPage().IsMatch(Clean(lines[headingLine - 1]))) return null;
                var years = YearToken().Matches(line).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
                if (years.Count > 2 && years.Count % 2 == 0 && years.Chunk(2).All(p => p[0] == years[0] && p[1] == years[1])) years = years[..2];
                if (years.Count > 2 && !line.Contains("ended", StringComparison.OrdinalIgnoreCase)) return null;
                if (years.Count == 2 && years[0] == years[1] + 1 && !line.Contains("ended", StringComparison.OrdinalIgnoreCase)) year ??= years[0];
                unit ??= Unit(line);
            }
            if (StatementEnd().IsMatch(line) && rows.Count > 0) break;
            if (IsYearsHeader(line)) continue;
            var parsed = Row(line, stripNote: true);
            if (parsed is { Label.Length: > 0, Values.Count: 2 } && Row(line, stripNote: false) is { Values.Count: 2 } && NoteAfterLabel(line))
            {
                // "Turnover - net  29  13,691,815" with this year's amount printed alone on the line above: the note
                // number isn't an amount. Without the lone amount there is only one year: not usable.
                parsed = loneAmount is { } above ? parsed with { Values = [above, parsed.Values[1]] } : null;
            }
            loneAmount = parsed is { Label.Length: 0, Values: [var only] } ? only : null;
            if (parsed is { } row && row.Values.Count >= 2)
            {
                if (year is null) return null;   // amounts before the column header: not a statement
                rows.Add((row.Label, row.Values, i));
            }
        }
        if (year is null || rows.Count < 3) return null;
        // A statement says which year it covers right under its heading; a "Statement of profit or loss" subheading in a
        // disclosure table (Shariah compliance, Islamic banking) doesn't.
        if (!Enumerable.Range(start, Math.Min(5, lines.Count - start)).Any(i => YearEnded().IsMatch(lines[i]))) return null;
        if (Enumerable.Range(Math.Max(0, headingLine - 3), headingLine - Math.Max(0, headingLine - 3)).Any(i => Annexure().IsMatch(lines[i]))) return null;
        unit ??= 1;   // "Rupees" columns, or no unit printed

        var revenue = RevenueRow(rows);
        if (revenue is null) return null;
        var profitIndex = rows.FindIndex(r => r.Line > revenue.Value.Line && ProfitRow().IsMatch(r.Label) && !r.Label.Contains("before", StringComparison.OrdinalIgnoreCase));
        if (profitIndex < 0) return null;
        var profit = rows[profitIndex];
        // Group accounts: the owners' share ("attributable to equity holders of the Holding Company"), like US net income.
        if (consolidated)
            foreach (var r in rows.Skip(profitIndex + 1).Take(6))
                if (OwnersShare().IsMatch(r.Label)) { profit = r; break; }
        var eps = rows.FirstOrDefault(r => r.Line > profit.Line && EpsRow().IsMatch(r.Label));
        if (eps.Label is null)
        {
            // "Earnings per share" on its own line, "- Basic  5.46  4.19" below.
            var basic = rows.FirstOrDefault(r => r.Line > profit.Line && r.Line <= profit.Line + 6 && BasicRow().IsMatch(r.Label));
            if (basic.Label is not null && Enumerable.Range(profit.Line, basic.Line - profit.Line).Any(l => EpsRow().IsMatch(Clean(lines[l])))) eps = basic;
        }

        var (rev, priorRev) = LastTwo(revenue.Value.Values);
        var (pat, priorPat) = LastTwo(profit.Values);
        if (rev <= 0) return null;
        decimal? Eps(int back) => eps.Label is null || eps.Values.Count < 2 || Math.Abs(eps.Values[^back]) >= 100_000 ? null : eps.Values[^back];
        return new PkStatement(consolidated, year.Value, periodEnd is { } d && d.Year == year ? d : periodEnd,
            rev * unit.Value, priorRev * unit.Value, pat * unit.Value, priorPat * unit.Value, Eps(2), Eps(1), revenue.Value.Label, headingLine);
    }

    /// <summary>
    /// The sales line: the last revenue/sales/turnover row before cost of sales ("Gross revenue", less sales tax, then "Net
    /// revenue"). Banks have no sales: their revenue is "Total income" (net mark-up plus fees); insurers show premiums.
    /// </summary>
    private static (string Label, List<decimal> Values, int Line)? RevenueRow(List<(string Label, List<decimal> Values, int Line)> rows)
    {
        var costAt = rows.FindIndex(r => CostRow().IsMatch(r.Label));
        // No cost of sales (an oil company deducts royalty and expenses): the sales line is among the first rows.
        var before = costAt < 0 ? rows.Take(8).ToList() : rows.Take(costAt).ToList();
        var sales = before.LastOrDefault(r => SalesRow().IsMatch(r.Label) && !NotSales().IsMatch(r.Label));
        if (sales.Label is not null) return sales;
        foreach (var pattern in new[] { TotalIncomeRow(), InsuranceRow(), InterestEarnedRow() })
        {
            var r = rows.FirstOrDefault(x => pattern.IsMatch(x.Label));
            if (r.Label is not null) return r;
        }
        return null;
    }

    private static (decimal Current, decimal Prior) LastTwo(List<decimal> v) => (v[^2], v[^1]);

    // ---- Chief executive's pay ------------------------------------------------------------------------------------

    /// <summary>
    /// The remuneration note is a table: column groups (Chief Executive, Directors, Executives… in any order) for two years —
    /// either each group split by year ("2025 2024 2025 2024") or each year split by group ("2025" over all groups, then
    /// "2024") — rows of pay items and a total. The chief executive's column is the one under the "Chief Executive" heading
    /// (by position on the page, when known); the total is trusted only when the items above it add up to it.
    /// </summary>
    private static PkCeoPay? ReadCeoPay(IReadOnlyList<PdfTextLine> lines, int heading, List<string> warnings)
    {
        var header = new List<PdfTextLine>();
        var rows = new List<(string Label, List<decimal> Values, int Line)>();
        decimal? unit = null;
        List<int>? years = null;
        for (var i = heading + 1; i < Math.Min(lines.Count, heading + 45); i++)
        {
            var line = Clean(lines[i].Text);
            if (rows.Count > 0 && NextNote().IsMatch(line)) break;
            var row = Row(line, stripNote: false);
            var yearsHeader = IsYearsHeader(line);
            if (rows.Count == 0 && (row is null || row.Values.Count < 2 || yearsHeader))
            {
                header.Add(lines[i] with { Text = line });
                unit ??= Unit(line);
                if (yearsHeader)
                {
                    var found = YearToken().Matches(line).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
                    if (years is null || found.Count > years.Count) years = found;
                }
                continue;
            }
            if (row is not null && row.Values.Count >= 2) rows.Add((row.Label, row.Values, i));
        }
        if (years is null || rows.Count < 2) return null;
        var year = years[0];
        // The unit can be printed above the note's heading ("(Amounts in Rs. '000)" at the top of the page).
        for (var i = heading - 1; unit is null && i >= Math.Max(0, heading - 4); i--) unit = Unit(Clean(lines[i].Text));
        // Columns split by year when the years line repeats ("2025 2024 2025 2024"); a single "2025  2024" over several
        // columns splits the years by group; one year = one table per year.
        var yearTokens = years.Count;

        // How many amount columns the table has: the most common count among its item rows.
        var columns = rows.Where(r => r.Label.Length > 0).GroupBy(r => r.Values.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).FirstOrDefault()?.Key ?? 0;
        if (columns < 2) return null;
        // A note number in front of the amounts ("Fees  (37.2)  -  -  11,600,000 …").
        for (var r = 0; r < rows.Count; r++)
            if (rows[r].Values.Count == columns + 1 && rows[r].Label.Length > 0 && Math.Abs(rows[r].Values[0]) is > 0 and < 100 && Math.Abs(rows[r].Values[0]) != Math.Floor(Math.Abs(rows[r].Values[0])))
                rows[r] = (rows[r].Label, rows[r].Values.Skip(1).ToList(), rows[r].Line);

        // The column headings: the lines after the introduction "… are as follows:".
        var intro = header.FindLastIndex(h => h.Text.Contains("follows", StringComparison.OrdinalIgnoreCase) || h.Text.EndsWith(':'));
        var headings = header.Skip(intro + 1).Where(h => h.Text.Split(' ').Length < 12 || h.Text.Contains("  ")).ToList();
        var byPosition = CeoColumnByPosition(lines, headings, rows, columns, yearTokens);
        var index = byPosition?.Index ?? CeoColumnByOrder(headings, columns, yearTokens);
        if (index is not { } ceo) return null;

        // The chief executive's amount in a row: its column in a full row; in a row with fewer amounts (an item that
        // wrapped onto two lines), the amount printed under the column — or nothing.
        decimal? CeoValue((string Label, List<decimal> Values, int Line) r)
        {
            if (r.Values.Count == columns) return r.Values[ceo];
            if (byPosition is not { } p) return null;
            var line = lines[r.Line];
            var cells = ColumnGap().Split(line.Text.Trim());
            if (cells.Length != line.CellCenters.Count) return null;
            var amounts = cells.Select((c, i) => (Value: Amount(c.Replace(" ", "")), X: line.CellCenters[i])).Where(x => x.Value is not null).ToList();
            if (amounts.Count != r.Values.Count) return null;
            var half = p.Spacing / 2;
            return amounts.FirstOrDefault(a => Math.Abs(a.X - p.Centers[ceo]) < half).Value ?? 0;
        }

        // More than one person in the chief executive's column (a change of CEO during the year): no single person's pay.
        var persons = rows.FirstOrDefault(r => PersonsRow().IsMatch(r.Label) && r.Values.Count == columns);
        if (persons.Label is not null && persons.Values[ceo] > 1)
        {
            warnings.Add($"remuneration note: {persons.Values[ceo]} people held the chief executive's post during the year");
            return null;
        }

        // Items since the last confirmed total (which stands in for everything above it), and every item read.
        var items = new List<(string Label, decimal Value)>();
        var allItems = new List<(string Label, decimal Value)>();
        var counted = new List<(string Label, decimal Value)>();
        decimal? total = null;
        var verified = false;
        foreach (var r in rows)
        {
            if (CeoValue(r) is not { } value || PersonsRow().IsMatch(r.Label)) continue;
            var full = r.Values.Count == columns;
            if (!full && r.Label.Length == 0 && value == 0) continue;   // the other columns' half of a wrapped item
            if (r.Label.Length == 0 && full || TotalRow().IsMatch(r.Label) || r.Label.Length == 0 && Math.Abs(items.Sum(x => x.Value) - value) <= 2)
            {
                // A line of amounts without a label (or "Total"): the total when the items add up to it. It can be a
                // subtotal ("short term benefits") with more items and a grand total below, so keep reading.
                var sum = items.Sum(x => x.Value);
                if (items.Count > 0 && Math.Abs(sum - value) <= Math.Max(2, value * 0.001m))
                {
                    total = value; verified = true; counted = allItems.ToList();
                    items = [("(subtotal)", value)];
                }
                else if (!verified) total ??= value;
                continue;
            }
            items.Add((r.Label, value));
            allItems.Add((r.Label, value));
        }
        items = counted;
        if (total is not { } t || t <= 0) return null;
        if (!verified) { warnings.Add("remuneration note: the chief executive's items don't add up to the total"); return null; }

        var u = unit ?? 1;
        // "Rupees" over the columns but "(Amounts in thousand)" printed elsewhere on the page: a CEO isn't paid Rs 126,070.
        if (u == 1 && t < 1_000_000 && Enumerable.Range(Math.Max(0, heading - 60), Math.Min(lines.Count, heading + 45) - Math.Max(0, heading - 60))
                .Any(i => InThousands().IsMatch(lines[i].Text)))
            u = 1000;
        var salary = items.Where(x => SalaryRow().IsMatch(x.Label)).Sum(x => x.Value);
        var bonus = items.Where(x => BonusRow().IsMatch(x.Label)).Sum(x => x.Value);
        var rupees = t * u;
        // A chief executive paid under Rs 1 million or over Rs 10 billion a year is a misread column or unit.
        if (rupees is < 1_000_000 or > 10_000_000_000) { warnings.Add($"remuneration note: implausible chief executive total Rs {rupees:N0}"); return null; }
        return new PkCeoPay(year, rupees, salary * u, bonus * u, verified, heading);
    }

    /// <summary>
    /// The amount column nearest the first "Chief Executive" heading on the page. A heading centred over its two years
    /// ("2025  2024") is nearest one of them: its first column (the report's own year) is the one.
    /// </summary>
    private static (int Index, List<double> Centers, double Spacing)? CeoColumnByPosition(IReadOnlyList<PdfTextLine> lines, List<PdfTextLine> headings,
        List<(string Label, List<decimal> Values, int Line)> rows, int columns, int yearTokens)
    {
        double? chief = null;
        foreach (var h in headings)
        {
            var cells = ColumnGap().Split(h.Text.Trim());
            if (cells.Length != h.CellCenters.Count) continue;
            var at = Array.FindIndex(cells, c => c.StartsWith("Chief", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Managing", StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { chief = h.CellCenters[at]; break; }
        }
        if (chief is null) return null;
        // The amount columns' positions, from any item row whose cells line up one amount per cell.
        foreach (var r in rows.Where(r => r.Values.Count == columns))
        {
            var line = lines[r.Line];
            var cells = ColumnGap().Split(line.Text.Trim());
            if (cells.Length != line.CellCenters.Count) continue;
            var amountCells = cells.Select((c, i) => (c, i)).Where(x => Amount(x.c.Replace(" ", "")) is not null).Select(x => line.CellCenters[x.i]).ToList();
            if (amountCells.Count != columns) continue;
            var nearest = amountCells.Select((x, i) => (Distance: Math.Abs(x - chief.Value), i)).MinBy(x => x.Distance).i;
            var spacing = amountCells.Zip(amountCells.Skip(1)).Select(p => p.Second - p.First).DefaultIfEmpty(60).Min();
            return (yearTokens >= columns ? nearest - nearest % 2 : nearest, amountCells, spacing);
        }
        return null;
    }

    /// <summary>Without positions: the groups named before "Chief Executive" in the headings, in reading order.</summary>
    private static int? CeoColumnByOrder(List<PdfTextLine> headings, int columns, int yearTokens)
    {
        var text = string.Join("  ", headings.Select(h => h.Text));
        var at = text.IndexOf("Chief", StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = text.IndexOf("Managing", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var before = GroupLabel().Matches(text[..at]).Count;
        var byYear = yearTokens >= columns;
        var groups = byYear ? columns / 2 : yearTokens == 1 ? columns : columns / Math.Max(1, yearTokens);
        if (before >= groups) return null;
        return byYear ? before * 2 : before;
    }

    // ---- Lines and amounts ----------------------------------------------------------------------------------------

    public sealed record ParsedRow(string Label, List<decimal> Values);

    /// <summary>
    /// "Profit after taxation  34  46,629,367  (33,092,162)" → label and amounts (a note number like "34" or "35.1" right
    /// after the label is dropped; "-" is zero; brackets are negative). Columns are separated by two spaces.
    /// </summary>
    public static ParsedRow? Row(string line, bool stripNote = true)
    {
        var cells = ColumnGap().Split(line.Trim()).Where(c => c.Length > 0).ToList();
        var values = new List<decimal>();
        var label = new List<string>();
        foreach (var raw in cells)
        {
            // Glyph spacing can split a number ("3,461,306,13 1"): a cell of number pieces is one number.
            var cell = raw.Replace(" ", "");
            if (Amount(cell) is { } v) { values.Add(v); continue; }
            if (values.Count > 0)
            {
                // Text after amounts ("12,345  (Restated)"): cells like "- -" hold several dashes.
                if (raw.Split(' ').All(p => Amount(p) is not null)) { values.AddRange(raw.Split(' ').Select(p => Amount(p)!.Value)); continue; }
                // Amounts, then the label, then amounts: a bank printing US-dollar columns on the left of the rupee ones.
                // The rupee amounts (after the label) are the statement's own.
                if (label.Count == 0) { values.Clear(); label.Add(raw); continue; }
                return null;
            }
            // A cell with several amounts joined by single spaces ("-  - 23,143,442,866").
            var parts = raw.Split(' ');
            if (parts.Length > 1 && parts.All(p => Amount(p) is not null) && label.Count > 0) { values.AddRange(parts.Select(p => Amount(p)!.Value)); continue; }
            label.Add(raw);
        }
        if (values.Count == 0) return null;
        // A note number right after the label ("27", "35.1", "(37.2)"): not an amount.
        var text = string.Join(" ", label);
        var noteMatch = TrailingNote().Match(text);
        if (noteMatch.Success) text = text[..noteMatch.Index].Trim();
        if (stripNote && label.Count > 0 && values.Count >= 3 && IsNoteNumber(cells[label.Count])) values.RemoveAt(0);
        return new ParsedRow(text, values);
    }

    private static bool IsNoteNumber(string cell) => NoteNumber().IsMatch(cell.Trim());

    /// <summary>"Label  29  13,691,815": the cell after the label is a note number.</summary>
    private static bool NoteAfterLabel(string line)
    {
        var cells = ColumnGap().Split(line.Trim());
        return cells.Length >= 3 && Amount(cells[0]) is null && IsNoteNumber(cells[1]) && !cells[1].Contains('(');
    }

    private static decimal? Amount(string cell)
    {
        if (cell is "-" or "–" or "—" or "--") return 0;
        var m = AmountPattern().Match(cell);
        if (!m.Success) return null;
        // Digits run together from neighbouring cells can overflow: not an amount.
        if (!decimal.TryParse(m.Groups["n"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null;
        return m.Groups["neg"].Success || cell.StartsWith('-') ? -n : n;
    }

    private static decimal? Unit(string line)
    {
        var l = line.ToLowerInvariant();
        if (!UnitWord().IsMatch(l)) return null;
        if (l.Contains("million") || MillionShort().IsMatch(l)) return 1_000_000;
        if (l.Contains("billion")) return 1_000_000_000;
        if (l.Contains("000") || l.Contains("thousand")) return 1_000;
        return 1;
    }

    private static DateOnly? PeriodEnd(string line)
    {
        var m = EndedOn().Match(line);
        if (!m.Success) return null;
        var text = m.Groups["date"].Value.Replace(",", " ");
        text = MultiSpace().Replace(text, " ").Trim();
        string[] formats = ["MMMM d yyyy", "d MMMM yyyy", "MMM d yyyy", "d MMM yyyy"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
    }

    /// <summary>"Note  2026  2025", "(Rupees in 000)  2025  2024  2025  2024": column years, with at most a note or unit label.</summary>
    private static bool IsYearsHeader(string line)
    {
        if (!YearToken().IsMatch(line) || line.Contains("ended", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = YearToken().Replace(UnitLabel().Replace(line, " "), " ");
        return rest.Trim().Length == 0;
    }

    /// <summary>Ligatures ("ﬁ") and odd spaces as plain text.</summary>
    public static string Clean(string line) => line.Normalize(System.Text.NormalizationForm.FormKC).Replace(' ', ' ').Trim();

    [GeneratedRegex(@"^(?<kind>unconsolidated|consolidated|standalone|separate)?\s*(statement\s+of\s+pro\S{1,2}t\s+(or|and)\s+loss(\s+account)?(\s+and\s+other\s+comprehensive\s+income)?|pro\S{1,2}t\s+(and|or)\s+loss\s+account|income\s+statement)$", RegexOptions.IgnoreCase)]
    private static partial Regex StatementHeading();
    [GeneratedRegex(@"^(note\s+)?(\d{1,3}(\.\d{1,2})*\.?\s+)?remuneration\s+of\s+(the\s+)?((chairman|chairperson|directors?)[,\s]+(and\s+)?)*(chief\s+executives?|managing\s+director)", RegexOptions.IgnoreCase)]
    private static partial Regex RemunerationHeading();
    [GeneratedRegex(@"annexed notes|^statement of comprehensive|comprehensive income$|^(chief )?financial officer|^chairman", RegexOptions.IgnoreCase)]
    private static partial Regex StatementEnd();
    [GeneratedRegex(@"^\d{1,2}(\.\d{1,2})?\.?\s+[A-Za-z]", RegexOptions.IgnoreCase)]
    private static partial Regex NextNote();
    [GeneratedRegex(@"\b20\d{2}\b")] private static partial Regex YearToken();
    [GeneratedRegex(@"rupees|\brs\b|\bpkr\b", RegexOptions.IgnoreCase)] private static partial Regex RupeeWord();
    [GeneratedRegex(@"(year|period)\s+ended|\bended\b|\bas\s+at\b", RegexOptions.IgnoreCase)] private static partial Regex YearEnded();
    [GeneratedRegex(@"islamic\s+banking|annexure|shariah|window\s+operations", RegexOptions.IgnoreCase)] private static partial Regex Annexure();
    [GeneratedRegex(@"^(head|corporate|principal)\s+office(\s+(and|&)\s+mailing(\s+address)?)?\s*[:/\-]?$", RegexOptions.IgnoreCase)] private static partial Regex HeadOfficeLabel();
    [GeneratedRegex(@"\bUS\s*\$|\bUSD\b|US\s+dollars?|\bin\s+US\b", RegexOptions.IgnoreCase)] private static partial Regex ForeignCurrency();
    [GeneratedRegex(@"(amounts|rupees|figures)\s+in\s+(thousand|'000|`000)", RegexOptions.IgnoreCase)] private static partial Regex InThousands();
    [GeneratedRegex(@"\bvs\.?\b|%\s*age|\banalysis\b", RegexOptions.IgnoreCase)] private static partial Regex AnalysisPage();
    [GeneratedRegex(@"\((rupees|rs\.?|pkr)[^)]*\)|\b(rupees|rs\.?|pkr)\b(\s+in)?(\s+[`'’‘]?000[`'’‘]?|\s+thousands?|\s+millions?)?|\bnotes?\b|\(?restated\)?", RegexOptions.IgnoreCase)]
    private static partial Regex UnitLabel();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex ColumnGap();
    [GeneratedRegex(@"\s+")] private static partial Regex MultiSpace();
    [GeneratedRegex(@"^(?<neg>\()?-?(?<n>\d{1,3}(,\d{2,3})*(\.\d+)?|\d+(\.\d+)?)\)?[%*]?$")] private static partial Regex AmountPattern();
    [GeneratedRegex(@"^\(?\d{1,2}(\.\d{1,2}){0,2}\)?$")] private static partial Regex NoteNumber();
    [GeneratedRegex(@"\s+\(?\d{1,2}(\.\d{1,2}){0,2}\)?$")] private static partial Regex TrailingNote();
    [GeneratedRegex(@"rupees|\brs\b|\bpkr\b|'000|`000|’000|‘000", RegexOptions.IgnoreCase)] private static partial Regex UnitWord();
    [GeneratedRegex(@"\bmn\b|\bm\)")] private static partial Regex MillionShort();
    [GeneratedRegex(@"(ended|ending)\s+(on\s+)?(?<date>(\d{1,2}\s+[A-Za-z]+,?\s+20\d{2})|([A-Za-z]+\s+\d{1,2},?\s+20\d{2}))", RegexOptions.IgnoreCase)]
    private static partial Regex EndedOn();
    [GeneratedRegex(@"^cost\s+of\s+(sales|revenue|goods|services|operations)|^direct\s+(costs?|expenses)|^gross\s+(pro\S{1,2}t|loss)|^operating\s+(costs|expenses)", RegexOptions.IgnoreCase)]
    private static partial Regex CostRow();
    [GeneratedRegex(@"\b(revenue|sales|turnover)\b", RegexOptions.IgnoreCase)] private static partial Regex SalesRow();
    [GeneratedRegex(@"^(gross|less)|sales\s+tax|excise|discount|commission|duty|returns|rebate|cost", RegexOptions.IgnoreCase)] private static partial Regex NotSales();
    [GeneratedRegex(@"^total\s+income", RegexOptions.IgnoreCase)] private static partial Regex TotalIncomeRow();
    [GeneratedRegex(@"^(net\s+)?(insurance\s+revenue|(insurance\s+)?premium\s+revenue|net\s+premium|insurance\s+premium)", RegexOptions.IgnoreCase)] private static partial Regex InsuranceRow();
    [GeneratedRegex(@"^(mark-?\s?up|markup|profit|interest).*earned", RegexOptions.IgnoreCase)] private static partial Regex InterestEarnedRow();
    [GeneratedRegex(@"^(net\s+)?(\(?loss\)?|\(?pro\S{1,2}t\)?|\(?income\)?)(\s*/\s*\(?(loss|pro\S{1,2}t|income)\)?)?\s+(after\s+(income\s+)?tax(ation|es)?|for\s+the\s+(year|period))", RegexOptions.IgnoreCase)]
    private static partial Regex ProfitRow();
    [GeneratedRegex(@"(owners|equity\s+holders|shareholders|members)\s+of\s+the\s+(holding|parent)", RegexOptions.IgnoreCase)] private static partial Regex OwnersShare();
    [GeneratedRegex(@"^(basic\s+)?\(?(earnings|loss)\)?[^0-9]{0,25}per\s+(ordinary\s+)?share", RegexOptions.IgnoreCase)] private static partial Regex EpsRow();
    [GeneratedRegex(@"^-?\s*basic", RegexOptions.IgnoreCase)] private static partial Regex BasicRow();
    [GeneratedRegex(@"^number\s+of\s+persons|^no\.?\s+of\s+persons|who\s+worked\s+part", RegexOptions.IgnoreCase)] private static partial Regex PersonsRow();
    [GeneratedRegex(@"^total\b", RegexOptions.IgnoreCase)] private static partial Regex TotalRow();
    [GeneratedRegex(@"managerial|basic\s+salary|^salar|^remuneration", RegexOptions.IgnoreCase)] private static partial Regex SalaryRow();
    [GeneratedRegex(@"bonus|incentive|performance", RegexOptions.IgnoreCase)] private static partial Regex BonusRow();
    [GeneratedRegex(@"chairman|chairperson|chief\s+executive|managing\s+director|executive\s+directors?|non[\s-]*executive\s+directors?|(?<!executive\s)directors?\b|executives|key\s+management", RegexOptions.IgnoreCase)]
    private static partial Regex GroupLabel();
}
