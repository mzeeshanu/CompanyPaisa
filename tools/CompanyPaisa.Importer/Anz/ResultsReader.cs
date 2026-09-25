using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Pk;

namespace CompanyPaisa.Importer.Anz;

/// <summary>A year's revenue and profit (owners' share) and the year before, in whole currency units.</summary>
/// <param name="Currency">"AUD", "NZD" or "USD" (many Australian miners report in US dollars).</param>
/// <param name="Source">Where they were read: "statement" (the statement of profit or loss) or "4E" (the results summary).</param>
public sealed record AnzResults(int FiscalYear, DateOnly? PeriodEnd, string Currency, decimal Revenue, decimal? PriorRevenue,
    decimal NetIncome, decimal? PriorNetIncome, string RevenueLine, string Source, int Line);

/// <summary>
/// Reads an Australian or New Zealand annual report's text (see <see cref="PdfLines"/>) by rules: the statement of
/// profit or loss ("Consolidated Statement of Profit or Loss and Other Comprehensive Income", "Income Statement",
/// "Statement of Financial Performance") — its "2026  2025" column header, its unit ("$m", "US$'000", "$") and its revenue
/// and profit rows — or, failing that, the Appendix 4E's "Results for announcement to the market" (this year only).
/// Anything that doesn't fit is left out rather than guessed.
/// </summary>
public static partial class ResultsReader
{
    public static AnzResults? Read(IReadOnlyList<PdfTextLine> pdfLines, string homeCurrency)
    {
        var lines = pdfLines.Select(l => AnnualReportReader.Clean(l.Text)).ToList();
        AnzResults? best = null;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsHeading(lines, i, out var used)) continue;
            if (ReadStatement(lines, i, i + used, homeCurrency) is not { } s) continue;
            // The group's statement comes first; a later one is the parent company's or a summary. Keep the first that reads.
            best = s;
            break;
        }
        var summary = ReadAppendix4E(lines, homeCurrency);
        if (best is null) return summary;
        // The 4E states this year's figures in full dollars: a check on the unit read from the statement.
        if (summary is not null && summary.FiscalYear == best.FiscalYear && best.Revenue > 0 &&
            summary.Revenue / best.Revenue is > 900 and < 1100 or < 0.0011m and > 0.0009m)
            return summary with { PriorRevenue = best.PriorRevenue * summary.Revenue / best.Revenue, PriorNetIncome = best.PriorNetIncome * summary.Revenue / best.Revenue };
        return best;
    }

    private static bool IsHeading(List<string> lines, int i, out int used)
    {
        used = 1;
        var one = lines[i];
        if (one.Length > 110) return false;
        var two = i + 1 < lines.Count ? $"{one} {lines[i + 1]}" : one;
        if (Heading().IsMatch(one)) return true;
        if (Heading().IsMatch(two)) { used = 2; return true; }
        return false;
    }

    private sealed record Row(string Label, List<decimal> Values, int Line);

    private static AnzResults? ReadStatement(List<string> lines, int heading, int start, string homeCurrency)
    {
        int? year = null;
        var oldestFirst = false;
        DateOnly? periodEnd = null;
        decimal? unit = null;
        string? currency = null;
        var rows = new List<Row>();
        var end = Math.Min(lines.Count, start + 80);
        for (var i = start; i < end; i++)
        {
            // Letters of sideways text printed down the page edge end up at the end of rows ("… 6,894  n").
            var line = StrayLetters().Replace(lines[i], "");
            if (rows.Count(r => r.Values.Count > 0) == 0 || year is null)
            {
                periodEnd ??= PeriodEnd(line);
                // The column header ("2026  2025", "Note  $m  $m") isn't a row. A contents page lists the statement with
                // a page number: no years header, so nothing is read.
                if (ColumnYears(line) is { } y) { year ??= Math.Abs(y); oldestFirst = y < 0; continue; }
                if (UnitOf(line) is { } u && AnnualReportReader.Row(line) is null) { unit ??= u.Scale; currency ??= u.Currency; continue; }
            }
            if (rows.Count > 0 && NextStatement().IsMatch(line)) break;
            var parsed = AnnualReportReader.Row(line);
            if (parsed is null) { if (IsSectionHeading(line)) rows.Add(new Row(line, [], i)); continue; }
            if (year is null) { if (parsed.Values.Count >= 2) return null; continue; }   // amounts before the years: not the statement
            if (parsed is { Label.Length: 0, Values.Count: 1 }) continue;   // a footnote marker ("1" over a restated column)
            var values = Columns(parsed.Values);
            if (values is { Count: 2 } && oldestFirst) values.Reverse();   // "2025  2026": this year is the second column
            if (values is null) continue;
            rows.Add(new Row(TrailingNote().Replace(parsed.Label, "").Trim(), values, i));
        }
        if (year is null || rows.Count(r => r.Values.Count > 0) < 4) return null;
        if (periodEnd is { } pe && pe.Year != year) periodEnd = null;
        // "For the year ended 30 September" with the year only over the columns (NAB, Air New Zealand).
        if (periodEnd is null)
            for (var i = start; i < Math.Min(lines.Count, start + 6) && periodEnd is null; i++)
                if (EndedDayMonth().Match(lines[i]) is { Success: true } dm &&
                    DateOnly.TryParseExact($"{dm.Groups["d"].Value} {dm.Groups["m"].Value} {year}", ["d MMMM yyyy", "d MMM yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    periodEnd = d;
        var revenue = RevenueOf(rows);
        var profit = ProfitOf(rows, revenue?.Row.Line ?? -1);
        if (revenue is null || profit is null || revenue.Value.Current <= 0) return null;
        var scale = unit ?? 1;
        var cur = currency ?? homeCurrency;
        var rev = revenue.Value.Current * scale;
        // A listed company with under $1,000 of sales, or over $1 trillion, is a misread unit.
        if (rev is < 1_000 or > 1_000_000_000_000) return null;
        return new AnzResults(year.Value, periodEnd, cur, rev, revenue.Value.Prior * scale, profit.Values[0] * scale,
            profit.Values.Count > 1 ? profit.Values[1] * scale : null, revenue.Value.Row.Label, "statement", heading);
    }

    /// <summary>This year and last year from a row's amounts: "note, 2026, 2025" or "2026, 2025", or the group's two of four.</summary>
    private static List<decimal>? Columns(List<decimal> v) => v.Count switch
    {
        1 => [v[0]],
        2 => [v[0], v[1]],
        3 => [v[1], v[2]],                       // a note number the parser couldn't tell from an amount
        4 => [v[0], v[1]],                       // group and parent company side by side
        _ => null
    };

    /// <summary>
    /// The sales line: "Revenue", "Sales revenue", "Revenue from contracts with customers", "Total revenue"; or, under a
    /// "Revenue" heading without amounts, the unlabelled total below its items (or the only item). Banks and insurers:
    /// total operating income or insurance revenue. Investment companies: total income from investments.
    /// </summary>
    private static (Row Row, decimal Current, decimal? Prior)? RevenueOf(List<Row> rows)
    {
        var costAt = rows.FindIndex(r => r.Values.Count > 0 && ExpenseRow().IsMatch(r.Label));
        var candidates = rows.Take(costAt < 0 ? rows.Count : costAt + 1).ToList();
        (Row, decimal, decimal?) Of(Row r) => (r, r.Values[0], r.Values.Count > 1 ? r.Values[1] : null);

        var totalRow = candidates.FirstOrDefault(r => r.Values.Count > 0 && TotalRevenueRow().IsMatch(r.Label));
        if (totalRow is not null) return Of(totalRow);
        var plain = candidates.FirstOrDefault(r => r.Values.Count > 0 && RevenueRow().IsMatch(r.Label));
        if (plain is not null) return Of(plain);

        // "Revenue" / "Operating revenue" as a heading: its items, then an unlabelled total or another heading.
        var at = candidates.FindIndex(r => r.Values.Count == 0 && RevenueHeading().IsMatch(r.Label));
        if (at >= 0)
        {
            // The items under the heading, up to their total (unlabelled or "Total …") or the next heading.
            var items = new List<Row>();
            Row? total = null;
            foreach (var r in rows.Skip(at + 1).Take(15))
            {
                if (r.Values.Count == 0 || ExpenseRow().IsMatch(r.Label)) break;
                if (r.Label.Length == 0 || r.Label.StartsWith("total", StringComparison.OrdinalIgnoreCase)) { total = r; break; }
                items.Add(r);
            }
            var heading = rows[at].Label;
            // "Revenue and other income": only the revenue items count (Worley's services, construction and procurement
            // revenue, not its other income and interest). "Revenue" / "Operating revenue": everything under it.
            var mixed = MixedHeading().IsMatch(heading);
            var sales = mixed ? items.Where(r => !OtherIncomeRow().IsMatch(r.Label) && !FinanceIncomeRow().IsMatch(r.Label)).ToList()
                              : items.TakeWhile(r => !OtherIncomeRow().IsMatch(r.Label) || total is not null).ToList();
            if (!mixed && total is not null) return (total with { Label = heading }, total.Values[0], total.Values.Count > 1 ? total.Values[1] : null);
            if (sales.Count == 1) return Of(sales[0]);
            if (sales.Count > 1 && sales.All(r => r.Values.Count == sales[0].Values.Count))
            {
                var current = sales.Sum(r => r.Values[0]);
                decimal? prior = sales[0].Values.Count > 1 ? sales.Sum(r => r.Values[1]) : null;
                return (sales[0] with { Label = heading }, current, prior);
            }
        }
        // Banks without a total: net interest income plus other operating income (NAB).
        var interest = rows.FindIndex(r => r.Values.Count > 0 && NetInterestRow().IsMatch(r.Label));
        var other = interest < 0 ? -1 : rows.FindIndex(interest + 1, r => r.Values.Count > 0 && BankOtherIncomeRow().IsMatch(r.Label));
        if (other > 0 && other - interest <= 3 && rows[interest].Values.Count == rows[other].Values.Count)
        {
            var (a, b) = (rows[interest], rows[other]);
            return (a with { Label = $"{a.Label} + {b.Label}" }, a.Values[0] + b.Values[0], a.Values.Count > 1 ? a.Values[1] + b.Values[1] : null);
        }
        foreach (var pattern in new[] { BankIncomeRow(), InsuranceRow(), InvestmentIncomeRow() })
            if (candidates.FirstOrDefault(r => r.Values.Count > 0 && pattern.IsMatch(r.Label)) is { } r) return Of(r);
        return null;
    }

    /// <summary>
    /// The profit that belongs to the company's shareholders: "Profit for the year attributable to members of the parent",
    /// or the owners' line under "Profit is attributable to:", else "Net profit after tax" / "Loss for the year".
    /// </summary>
    private static Row? ProfitOf(List<Row> rows, int after)
    {
        var body = rows.Where(r => r.Line > after && r.Values.Count > 0).ToList();
        var owners = body.FirstOrDefault(r => ProfitRow().IsMatch(r.Label) && OwnersWords().IsMatch(r.Label) && !r.Label.Contains("comprehensive", StringComparison.OrdinalIgnoreCase));
        if (owners is not null) return owners;
        // "Attributable to owners of the Company" on its own line under the year's profit.
        var bare = body.FirstOrDefault(r => BareOwnersRow().IsMatch(r.Label));
        if (bare is not null) return bare;
        // "Profit/(loss) for the year is attributable to:" then "Owners of X Limited" (the first such block, before the
        // comprehensive income one).
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Line <= after || !AttributableHeading().IsMatch(rows[i].Label) || rows[i].Label.Contains("comprehensive", StringComparison.OrdinalIgnoreCase)) continue;
            var owner = rows.Skip(i + 1).Take(3).FirstOrDefault(r => r.Values.Count > 0 && OwnersLine().IsMatch(r.Label));
            if (owner is not null) return owner;
        }
        return body.FirstOrDefault(r => ProfitRow().IsMatch(r.Label) && !NotNetProfit().IsMatch(r.Label));
    }

    /// <summary>A label line with no amounts that opens a group of rows ("Revenue", "Expenses", "Profit is attributable to:").</summary>
    private static bool IsSectionHeading(string line) => line.Length is > 3 and < 90 && !line.Any(char.IsDigit) && (RevenueHeading().IsMatch(line) || AttributableHeading().IsMatch(line) || OtherIncomeRow().IsMatch(line) || ExpenseRow().IsMatch(line));

    // ---- Appendix 4E -------------------------------------------------------------------------------------------------

    /// <summary>
    /// "Revenues from ordinary activities  Decreased 1%  To  94,024,398" and "Net profit … attributable to members … To …":
    /// the results summary every ASX company files at year end. This year only; the unit is printed nearby ("$'000", "A$").
    /// </summary>
    private static AnzResults? ReadAppendix4E(List<string> lines, string homeCurrency)
    {
        var at = lines.FindIndex(l => ResultsForAnnouncement().IsMatch(l));
        if (at < 0 || at > 400) return null;
        decimal? revenue = null, profit = null;
        var revenueLine = "";
        decimal scale = 1;
        var unitSeen = false;
        string? currency = null;
        int? year = null;
        DateOnly? periodEnd = null;
        for (var i = Math.Max(0, at - 40); i < Math.Min(lines.Count, at + 30); i++)
        {
            var line = lines[i];
            periodEnd ??= PeriodEnd(line);
            var amount = SummaryAmount(line);
            // The unit is a column heading ("$M", "$’000", "$M  $M  $M  %"); "to $3,895" is an amount, not the unit. The
            // nearest heading above the figures wins.
            if (i <= at + 12 && UnitOf(line) is { } u && !Regex.IsMatch(YearToken().Replace(UnitPattern().Replace(line, " "), " "), @"\d")) { scale = u.Scale; currency ??= u.Currency; unitSeen = true; continue; }
            if (i < at) continue;
            if (amount is null) continue;
            if (revenue is null && SummaryRevenue().IsMatch(line)) { revenue = amount; revenueLine = SummaryRevenue().Match(line).Value; }
            else if (profit is null && SummaryProfit().IsMatch(line) && !line.Contains("before", StringComparison.OrdinalIgnoreCase)) profit = amount * (Down().IsMatch(line) && LossWord().IsMatch(line) ? -1 : 1);
        }
        year = periodEnd?.Year;
        if (revenue is not { } rev || profit is not { } pat || year is null || rev <= 0) return null;
        // No unit printed and a small number: it's more likely millions or thousands than a tiny company — don't guess.
        if (!unitSeen && rev < 1_000_000) return null;
        // A summary is only filed by an operating company; under 100,000 of revenue means the unit was misread.
        if (rev * scale < 100_000) return null;
        return new AnzResults(year.Value, periodEnd, currency ?? homeCurrency, rev * scale, null, pat * scale, null, revenueLine, "4E", at);
    }

    /// <summary>
    /// "… up 29% to 2,796,117": the amount after "to". A table row ("Revenue  12,212  12,058  154  1.3" — this year, last
    /// year, change, % change): the first amount.
    /// </summary>
    private static decimal? SummaryAmount(string line)
    {
        // Percentages are the change, never the amount ("Increase  17.6%  938,763").
        line = Percent().Replace(line, " ");
        var to = ToWord().Matches(line).LastOrDefault();
        // Glyph spacing can split a number after "to" ("to 1 0,589.0"): the digits belong together.
        if (to is not null && AmountAtEnd(SplitDigits().Replace(line[(to.Index + to.Length)..].Trim(), "")) is { } after) return after;
        if (to is null && AnnualReportReader.Row(line, stripNote: false) is { Label.Length: > 0, Values.Count: >= 2 } row) return row.Values[0];
        return AmountAtEnd(line);
    }

    /// <summary>"… To  94,024,398" / "… to $12.3m" / "… 1,234": the last amount on the line.</summary>
    private static decimal? AmountAtEnd(string line)
    {
        var m = TrailingAmount().Match(line);
        if (!m.Success) return null;
        if (!decimal.TryParse(m.Groups["n"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) return null;
        var mult = m.Groups["unit"].Value.ToLowerInvariant() switch { "m" or "million" or "mn" => 1_000_000m, "k" or "000" => 1_000m, "b" or "billion" or "bn" => 1_000_000_000m, _ => 1m };
        return (m.Groups["neg"].Success ? -n : n) * mult;
    }

    // ---- Columns, units, dates ---------------------------------------------------------------------------------------

    /// <summary>"2026  2025", "30 June 2026  30 June 2025", "FY26  FY25", "Note  2026  2025  2026  2025": the report's own year.</summary>
    private static int? ColumnYears(string line)
    {
        if (line.Length > 120) return null;
        var fy = FyColumn().Matches(line).Select(m => 2000 + int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture)).ToList();
        var years = fy.Count >= 2 ? fy : YearToken().Matches(line).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        // Newest first ("2026  2025"), or oldest first ("2025  2026", returned negative so the caller swaps the columns).
        if (years.Count < 2 || Math.Abs(years[0] - years[1]) != 1) return null;
        // Only years, dates, notes and units on the line: a column header, not a sentence that mentions two years.
        var rest = ColumnWords().Replace(YearToken().Replace(FyColumn().Replace(line, " "), " "), " ");
        return rest.Trim().Length == 0 ? years[0] > years[1] ? years[0] : -years[1] : null;
    }

    /// <summary>"$m", "US$'000", "NZ$000", "$ million", "A$", "$": the multiplier and, when printed, the currency.</summary>
    private static (decimal Scale, string? Currency)? UnitOf(string line)
    {
        if (line.Length > 120) return null;
        var m = UnitPattern().Match(line);
        if (!m.Success) return null;
        var cur = m.Groups["cur"].Value.ToUpperInvariant() switch
        {
            "US" or "USD" or "US$" => "USD", "NZ" or "NZD" or "NZ$" => "NZD", "A" or "AUD" or "A$" or "AU" => "AUD", _ => null
        };
        var size = m.Groups["size"].Value.ToLowerInvariant().Replace("'", "").Replace("’", "").Replace("‘", "").Replace("`", "");
        decimal scale = size switch
        {
            "m" or "million" or "millions" or "mn" or "mil" => 1_000_000m,
            "000" or "000s" or "thousand" or "thousands" or "k" => 1_000m,
            "b" or "bn" or "billion" => 1_000_000_000m,
            _ => 1m
        };
        return (scale, cur);
    }

    private static DateOnly? PeriodEnd(string line)
    {
        var m = EndedOn().Match(line);
        if (!m.Success) return null;
        var text = Regex.Replace(m.Groups["date"].Value.Replace(",", " "), @"\s+", " ").Trim();
        string[] formats = ["d MMMM yyyy", "MMMM d yyyy", "d MMM yyyy", "MMM d yyyy"];
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null;
    }

    [GeneratedRegex(@"^(consolidated\s+|group\s+)?(statements?\s+of\s+(profit\s+(or|and)\s+loss|comprehensive\s+income|financial\s+performance|income)(\s+and\s+other\s+comprehensive\s+income)?|income\s+statements?|profit\s+(and|or)\s+loss\s+statement)(\s+for\s+the\s+(financial\s+)?year\s+ended.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();
    [GeneratedRegex(@"^(consolidated\s+)?(statements?\s+of\s+(financial\s+position|changes\s+in\s+equity|cash\s+flows?)|balance\s+sheets?|cash\s+flow\s+statements?)", RegexOptions.IgnoreCase)]
    private static partial Regex NextStatement();
    [GeneratedRegex(@"\b20\d{2}\b")] private static partial Regex YearToken();
    [GeneratedRegex(@"\bto\b", RegexOptions.IgnoreCase)] private static partial Regex ToWord();
    [GeneratedRegex(@"(?<=\d)\s(?=[\d,])")] private static partial Regex SplitDigits();
    [GeneratedRegex(@"[(>]?[-+]?\d+(\.\d+)?\s?%\)?")] private static partial Regex Percent();
    [GeneratedRegex(@"ended\s+(?<d>\d{1,2})\s+(?<m>January|February|March|April|May|June|July|August|September|October|November|December)\b", RegexOptions.IgnoreCase)] private static partial Regex EndedDayMonth();
    [GeneratedRegex(@"(?<=\d\)?|-)(\s{2,}[A-Za-z](\s+[A-Za-z])?)+$")] private static partial Regex StrayLetters();
    /// <summary>Note references after a label: "2(a)", "1G", "5A", "3.2", "16".</summary>
    [GeneratedRegex(@"(\s+\(?\d{1,2}(\.\d{1,2})*[A-Za-z]?(\([a-z]{1,3}\))?\)?)+$")] private static partial Regex TrailingNote();
    [GeneratedRegex(@"\bFY\s?(?<y>\d{2})\b", RegexOptions.IgnoreCase)] private static partial Regex FyColumn();
    [GeneratedRegex(@"\b(for|the|financial|notes?|consolidated|group|company|parent|restated\*?|\*|actual|audited|year|ended|to|30|31|28|29|1|january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sep|sept|oct|nov|dec)\b|[a-z]{0,3}\$\s?[’'‘`]?(000s?|m|million|mn)?|\(|\)|,|\s-\s", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnWords();
    [GeneratedRegex(@"(?<![a-z])(?<cur>US|USD|NZ|NZD|A|AU|AUD)?\s?\$\s?(?<size>[’'‘`]?\s?(000s?|m\b|million|millions|mn|mil\b|b\b|bn|billion|k\b))?|\b(?<cur>USD|NZD|AUD)\s?(?<size>[’'‘`]?(000s?|m\b|million|millions))", RegexOptions.IgnoreCase)]
    private static partial Regex UnitPattern();
    [GeneratedRegex(@"(ended|ending)\s+(on\s+)?(?<date>\d{1,2}\s+[A-Za-z]+,?\s+20\d{2}|[A-Za-z]+\s+\d{1,2},?\s+20\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex EndedOn();

    [GeneratedRegex(@"^total\s+(sales\s+)?revenues?(\s+from\s+(contracts\s+with\s+customers|continuing\s+operations|ordinary\s+activities|operations))?(\s*\(.*\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TotalRevenueRow();
    [GeneratedRegex(@"^((sales|operating|net)\s+)?revenues?(\s+from\s+(contracts\s+with\s+customers|continuing\s+operations|ordinary\s+activities|operations|the\s+sale\s+of\s+goods|sale\s+of\s+goods|services))?(\s*\(.*\))?$|^(net\s+)?sales(\s+revenue)?$|^turnover$", RegexOptions.IgnoreCase)]
    private static partial Regex RevenueRow();
    [GeneratedRegex(@"^((operating|sales)\s+)?revenues?(\s+from\s+contracts\s+with\s+customers)?(\s+(and|&)\s+(other\s+)?income)?:?$|^income:?$", RegexOptions.IgnoreCase)]
    private static partial Regex RevenueHeading();
    [GeneratedRegex(@"(and|&)\s+(other\s+)?income|^income:?$", RegexOptions.IgnoreCase)] private static partial Regex MixedHeading();
    [GeneratedRegex(@"^(interest|finance|dividend)\s+(income|revenue)|gains?\b|fair\s+value|share\s+of", RegexOptions.IgnoreCase)] private static partial Regex FinanceIncomeRow();
    [GeneratedRegex(@"^other\s+(income|revenue|gains)|^(total\s+)?other\s+income", RegexOptions.IgnoreCase)] private static partial Regex OtherIncomeRow();
    [GeneratedRegex(@"^(cost\s+of\s+(sales|goods|revenue)|expenses?:?$|operating\s+expen|(total\s+)?expenses|employee|raw\s+materials|materials|changes\s+in\s+inventor|depreciation|administrat|research)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpenseRow();
    [GeneratedRegex(@"^(total\s+)?(net\s+)?operating\s+income|^total\s+income$|^net\s+(interest\s+and\s+)?(banking\s+)?income$", RegexOptions.IgnoreCase)] private static partial Regex BankIncomeRow();
    [GeneratedRegex(@"^(net\s+)?insurance\s+(revenue|premium)|^(gross\s+)?(written\s+)?premium\s+revenue", RegexOptions.IgnoreCase)] private static partial Regex InsuranceRow();
    [GeneratedRegex(@"^total\s+(investment\s+)?(income|revenue)(\s+from\s+investments)?|^total\s+revenue\s+and\s+other\s+income|^revenue\s+and\s+other\s+income", RegexOptions.IgnoreCase)] private static partial Regex InvestmentIncomeRow();

    [GeneratedRegex(@"^(net\s+)?(\(?(loss|profit|earnings|income|deficit|surplus)\)?\s*[/&]?\s*)+(after\s+(income\s+)?tax(ation)?(\s+(expense|benefit))?|for\s+the\s+(financial\s+)?(year|period)|from\s+ordinary\s+activities\s+after\s+tax|attributable)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfitRow();
    [GeneratedRegex(@"attributable\s+to\s+(the\s+)?(members|owners|equity\s+holders|ordinary\s+(equity\s+)?holders|shareholders|stapled\s+security\s+holders|unitholders)", RegexOptions.IgnoreCase)]
    private static partial Regex OwnersWords();
    [GeneratedRegex(@"attributable\s+to:?$", RegexOptions.IgnoreCase)] private static partial Regex AttributableHeading();
    [GeneratedRegex(@"^(net\s+profit\s+)?attributable\s+to\s+(the\s+)?(owners|members|equity\s+holders|shareholders|ordinary\s+shareholders)\b", RegexOptions.IgnoreCase)] private static partial Regex BareOwnersRow();
    [GeneratedRegex(@"^net\s+interest\s+income$", RegexOptions.IgnoreCase)] private static partial Regex NetInterestRow();
    [GeneratedRegex(@"^(total\s+)?(other\s+operating\s+income|non-?interest\s+income|other\s+(banking\s+)?income)$", RegexOptions.IgnoreCase)] private static partial Regex BankOtherIncomeRow();
    [GeneratedRegex(@"^(owners|members|equity\s+holders|shareholders|ordinary\s+shareholders|stapled\s+security\s+holders)\b|^(the\s+)?(parent|company)\b|(limited|ltd|group|holdings)$", RegexOptions.IgnoreCase)]
    private static partial Regex OwnersLine();
    [GeneratedRegex(@"before|comprehensive|discontinued|continuing|non-?controlling|per\s+share|operating\s+profit|gross", RegexOptions.IgnoreCase)] private static partial Regex NotNetProfit();

    [GeneratedRegex(@"results\s+for\s+announcement\s+to\s+the\s+market", RegexOptions.IgnoreCase)] private static partial Regex ResultsForAnnouncement();
    [GeneratedRegex(@"^(\d(\.\d)?\s*)?(total\s+)?revenues?(\s+from\s+(ordinary|continuing)\s+(activities|operations))?", RegexOptions.IgnoreCase)] private static partial Regex SummaryRevenue();
    [GeneratedRegex(@"^(\d(\.\d)?\s*)?(net\s+)?(\(?(profit|loss)\)?\s*/?\s*)+.*(attributable\s+to\s+(the\s+)?(members|owners|shareholders)|after\s+tax)", RegexOptions.IgnoreCase)] private static partial Regex SummaryProfit();
    [GeneratedRegex(@"\b(down|decrease[sd]?|fell)\b", RegexOptions.IgnoreCase)] private static partial Regex Down();
    [GeneratedRegex(@"\bloss\b", RegexOptions.IgnoreCase)] private static partial Regex LossWord();
    [GeneratedRegex(@"(?<neg>\()?\$?\s?(?<n>\d{1,3}(,\d{3})+(\.\d+)?|\d+(\.\d+)?)\s?(?<unit>m|million|mn|k|b|bn|billion)?\)?\s*$", RegexOptions.IgnoreCase)] private static partial Regex TrailingAmount();
}
