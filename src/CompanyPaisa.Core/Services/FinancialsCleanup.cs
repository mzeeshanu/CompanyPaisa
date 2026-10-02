using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// Corrects figures that are off by a factor of 1,000 — a filing tagged in thousands as if in units, or the reverse — and
/// the fourth quarters worked out from them. Judged only against the company's own figures, the way an analyst would spot
/// them: Tigo Energy's 2024 revenue of $54.0 billion beside quarters of $10–14 million; The RealReal's 2021 "profit" of
/// $236 billion beside quarterly losses of $56–71 million; Luve's 2023 revenue of €615,823 between €617 million and
/// €587 million. A figure is changed only when it sits almost exactly 1,000× (or ÷1,000) from what its own quarters or
/// both neighbouring years say; otherwise it is left as filed. Used by the data store as figures load.
/// </summary>
public static class FinancialsCleanup
{
    public sealed record Result(IReadOnlyList<FinancialPeriod> Financials, int Rescaled, int QuartersRedone, int QuartersDropped);

    /// <summary>How close to 1,000× a figure must be to count as a unit slip (both ways).</summary>
    private const decimal Low = 600, High = 1700;

    public static Result Apply(IReadOnlyList<FinancialPeriod> financials)
    {
        var rescaled = 0;
        var redone = 0;
        var dropped = 0;
        var output = new List<FinancialPeriod>(financials.Count);
        foreach (var company in financials.GroupBy(f => f.CompanyId, StringComparer.OrdinalIgnoreCase))
        {
            var annual = company.Where(f => f.PeriodType == PeriodType.Annual).OrderBy(f => f.FiscalYear).ToList();
            var quarters = company.Where(f => f.PeriodType == PeriodType.Quarterly).GroupBy(f => f.FiscalYear)
                .ToDictionary(g => g.Key, g => g.OrderBy(q => q.FiscalQuarter).ToList());
            var original = annual.ToDictionary(a => a.FiscalYear);

            // 1. Annual figures: first against the year's own quarters, then against both neighbouring years, then (profit
            //    only) against the year's revenue.
            for (var i = 0; i < annual.Count; i++)
            {
                var a = annual[i];
                var q = quarters.GetValueOrDefault(a.FiscalYear) ?? [];
                // The quarters judge one measure only when they agree with the year on the other: when the quarters are
                // the ones in the wrong unit (Palatin 2016) nothing confirms them, and the year is left as filed.
                var revenueEstimate = FromQuarters(q, f => f.Revenue);
                var incomeEstimate = FromQuarters(q, f => f.NetIncome);
                var revenue = Fix(a.Revenue, Agrees(a.NetIncome, incomeEstimate, 10) ? revenueEstimate : null, Neighbours(annual, i, f => f.Revenue), null);
                var incomeNeighbours = Neighbours(annual, i, f => f.NetIncome);
                var income = Fix(a.NetIncome, Agrees(revenue, revenueEstimate, 2) ? incomeEstimate : null, incomeNeighbours, revenue, ProfitConfirmed(annual, i));
                // A year filed in the wrong unit is wrong throughout: Netcompany's 2022 report gave DKK 5,544,600 of revenue
                // and 603,400 of profit, both in thousands. Profit follows revenue's correction when that puts it in line
                // with both neighbouring years (profit swings too much year to year for the 1,000× test on its own).
                if (revenue != a.Revenue && income == a.NetIncome && a.Revenue != 0 && incomeNeighbours is { } n &&
                    a.NetIncome * (revenue / a.Revenue) is var scaled && Near(scaled, n.Prev, 10) && Near(scaled, n.Next, 10))
                    income = scaled;
                else if (income == a.NetIncome && ProfitInThousands(annual, i, revenue))
                    income /= 1000;
                if (income != a.NetIncome) income = Sign(income, revenue, q);
                if (revenue == a.Revenue && income == a.NetIncome) continue;
                annual[i] = a with { Revenue = revenue, NetIncome = income };
                rescaled++;
            }

            // A profit between two years whose profits were both filed 1,000× too big is in the same unit when ÷1,000 puts it
            // in line with them: OPKO's 2024 loss of $53 billion between corrected losses of $189 million and $226 million.
            for (var i = 1; i < annual.Count - 1; i++)
            {
                var (prev, a, next) = (annual[i - 1], annual[i], annual[i + 1]);
                if (next.FiscalYear - prev.FiscalYear != 2 || a.NetIncome != original[a.FiscalYear].NetIncome) continue;
                if (prev.NetIncome * 1000 != original[prev.FiscalYear].NetIncome || next.NetIncome * 1000 != original[next.FiscalYear].NetIncome) continue;
                if (Math.Abs(a.NetIncome) <= Math.Abs(a.Revenue) * 10 || !Near(a.NetIncome / 1000, prev.NetIncome, 10) || !Near(a.NetIncome / 1000, next.NetIncome, 10)) continue;
                annual[i] = a with { NetIncome = a.NetIncome / 1000 };
                rescaled++;
            }

            // Years at either end of the run have one neighbour, so the test above cannot judge them; there both revenue and
            // profit must sit 1,000× (or 1,000,000×) from the adjacent year — a young company's tiny first sales come with a
            // loss on its own scale. Moury Construct's 2020–22 figures in thousands (€155,000 of sales before €194 million),
            // Scandinavian Tobacco's 2025 (DKK 2.9 million after 2.9 billion), Lenzing's 2019 in millions (€2,105).
            var fixedHere = new HashSet<int>();
            for (var i = annual.Count - 2; i >= 0; i--)
                if (EdgeSlip(annual, i, i + 1, i - 1, fixedHere) is { } k) { annual[i] = Scale(annual[i], k); fixedHere.Add(i); rescaled++; }
            for (var i = 1; i < annual.Count; i++)
                if (EdgeSlip(annual, i, i - 1, i + 1, fixedHere) is { } k) { annual[i] = Scale(annual[i], k); fixedHere.Add(i); rescaled++; }

            // 2. Fourth quarters: one worked out as "year minus the first three" follows its corrected year; one far out of
            //    line with the year's other quarters is replaced by that difference when the difference fits, else left out.
            foreach (var (year, q) in quarters)
            {
                var q4 = q.FirstOrDefault(f => f.FiscalQuarter == 4);
                var first3 = q.Where(f => f.FiscalQuarter is >= 1 and <= 3).ToList();
                if (q4 is null || first3.Count != 3 || annual.FirstOrDefault(a => a.FiscalYear == year) is not { } year1) continue;
                var old = original[year];
                var at = q.IndexOf(q4);
                var fixedQ4 = q4;
                foreach (var (get, set) in Measures)
                {
                    var rest = first3.Sum(get);
                    var worked = get(old) - rest;
                    var stored = get(fixedQ4);
                    if (get(year1) != get(old) && Math.Abs(stored - worked) <= 1)
                    {
                        fixedQ4 = set(fixedQ4, get(year1) - rest);
                        continue;
                    }
                    // Revenue only: a fourth quarter at under 1/100 of the other three, for a company whose quarters run
                    // over $100 million — 3M's 2022 year was restated without Solventum while its quarters were not, leaving
                    // "Q4" at $11 million. Smaller companies have genuinely lumpy quarters (uranium sales, licence fees), and a
                    // big fourth quarter can be real (Zymeworks' upfront payment), so neither is judged. Profit swings too
                    // much quarter to quarter to judge this way; a negative quarter is left alone (insurers report them).
                    if (get != Measures[0].Get) continue;
                    var median = first3.Select(get).Order().ElementAt(1);
                    if (median < 100_000_000 || stored <= 0 || stored / median >= 0.01m) continue;
                    var expected = get(year1) - rest;
                    if (expected > 0 && expected / median is > 0.2m and < 5)
                    {
                        // The whole quarter came from somewhere else (Carnival's 2019 "Q4": $35 million of sales and a
                        // $2.2 billion loss): its profit is the year's less the first three quarters' too.
                        fixedQ4 = fixedQ4 with { Revenue = expected, NetIncome = year1.NetIncome - first3.Sum(f => f.NetIncome) };
                        break;
                    }
                    fixedQ4 = null;
                    break;
                }
                if (fixedQ4 is null) { q.RemoveAt(at); dropped++; }
                else if (fixedQ4 != q4) { q[at] = fixedQ4; redone++; }
            }

            output.AddRange(annual);
            output.AddRange(quarters.Values.SelectMany(q => q));
        }
        return new Result(output, rescaled, redone, dropped);
    }

    private static readonly (Func<FinancialPeriod, decimal> Get, Func<FinancialPeriod, decimal, FinancialPeriod> Set)[] Measures =
    [
        (f => f.Revenue, (f, v) => f with { Revenue = v }),
        (f => f.NetIncome, (f, v) => f with { NetIncome = v })
    ];

    /// <summary>
    /// The figure divided (or multiplied) by 1,000 when the year's quarters or both neighbouring years put it almost exactly
    /// that far out; a profit also when it is over 100× its year's revenue and ÷1,000 makes it ordinary. The sign follows
    /// the filing (see <see cref="Sign"/>).
    /// </summary>
    private static decimal Fix(decimal value, decimal? quarterEstimate, (decimal Prev, decimal Next)? neighbours, decimal? revenue, bool profitConfirmed = false)
    {
        if (value == 0) return value;
        if (quarterEstimate is { } est && est != 0)
        {
            var factor = value / est;
            if (Math.Abs(factor) is > Low and < High) return value / 1000;
            if (Math.Abs(factor) is > 1 / High and < 1 / Low) return value * 1000;
            return value;
        }
        if (neighbours is { } n && n.Prev != 0 && n.Next != 0 && Math.Sign(n.Prev) == Math.Sign(value) && Math.Sign(n.Next) == Math.Sign(value))
        {
            var (a, b) = (value / n.Prev, value / n.Next);
            if (a is > Low and < High && b is > Low and < High) return value / 1000;
            if (a is > 1 / High and < 1 / Low && b is > 1 / High and < 1 / Low) return value * 1000;
        }
        if (revenue is { } rev && rev >= 10_000_000 && Math.Abs(value) > rev * 100 && Math.Abs(value) / 1000 < rev * 10 && !profitConfirmed)
            return value / 1000;
        return value;
    }

    /// <summary>
    /// The corrected year's profit keeps its filed sign unless that leaves an impossible fourth quarter: The RealReal's 2021
    /// "profit" of $236 million after three quarterly losses would need a fourth-quarter profit of $420 million on $145
    /// million of sales; as a loss the fourth quarter is −$52 million. (G-III's 2022 loss after three profitable quarters —
    /// an impairment — keeps its sign: its fourth quarter is plausible either way round.)
    /// </summary>
    private static decimal Sign(decimal income, decimal revenue, List<FinancialPeriod> q)
    {
        var first3 = q.Where(f => f.FiscalQuarter is >= 1 and <= 3).ToList();
        if (first3.Count != 3) return income;
        var q4Revenue = revenue - first3.Sum(f => f.Revenue);
        if (q4Revenue <= 0) return income;
        var rest = first3.Sum(f => f.NetIncome);
        var asFiled = Math.Abs(income - rest);
        var flipped = Math.Abs(-income - rest);
        return asFiled > q4Revenue * 1.5m && flipped <= q4Revenue ? -income : income;
    }

    /// <summary>
    /// True when an adjacent year's profit is on the same scale as this year's and believable against its own revenue: then a
    /// profit far above revenue is real and the revenue is what is out (Solstad's "revenue" of NOK 10 million beside a NOK
    /// 1.1 billion loss, after another NOK 1.1 billion loss in 2022). A neighbour in the same wrong unit confirms nothing
    /// (American Superconductor's 2021 and 2022 losses, both filed 1,000× too big).
    /// </summary>
    private static bool ProfitConfirmed(List<FinancialPeriod> annual, int i) =>
        Adjacent(annual, i).Any(n => Near(annual[i].NetIncome, n.NetIncome, 10) && Math.Abs(n.NetIncome) <= Math.Abs(n.Revenue) * 30);

    /// <summary>
    /// A profit over 10× its year's revenue that ÷1,000 puts in line with an ordinary adjacent year (one whose profit is
    /// within 10× its revenue — earlier years are already corrected): OPKO's 2021 loss of $30.1 billion on $1.8 billion
    /// of sales beside a 2020 profit of $31 million; its 2024 loss of $53 billion beside 2023's corrected $189 million.
    /// </summary>
    private static bool ProfitInThousands(List<FinancialPeriod> annual, int i, decimal revenue)
    {
        var income = annual[i].NetIncome;
        return revenue >= 10_000_000 && Math.Abs(income) > revenue * 10 && Math.Abs(income) / 1000 <= revenue * 10 && !ProfitConfirmed(annual, i) &&
               Adjacent(annual, i).Any(n => Math.Abs(n.NetIncome) <= Math.Abs(n.Revenue) * 10 && Near(income / 1000, n.NetIncome, income > 0 ? 10 : 2));
    }

    private static IEnumerable<FinancialPeriod> Adjacent(List<FinancialPeriod> annual, int i) =>
        new[] { i - 1, i + 1 }.Where(j => j >= 0 && j < annual.Count && Math.Abs(annual[j].FiscalYear - annual[i].FiscalYear) == 1).Select(j => annual[j]);

    /// <summary>
    /// The factor that brings year <paramref name="i"/> in line with the adjacent year <paramref name="anchor"/> when it is an
    /// end year (or every year beyond it is out the same way) and the anchor is trustworthy — itself corrected, or in line
    /// with the year after it.
    /// </summary>
    private static decimal? EdgeSlip(List<FinancialPeriod> annual, int i, int anchor, int beyond, HashSet<int> fixedHere)
    {
        var (a, n) = (annual[i], annual[anchor]);
        if (fixedHere.Contains(i) || Math.Abs(a.FiscalYear - n.FiscalYear) != 1 || a.Revenue <= 0 || n.Revenue <= 0) return null;
        var further = anchor + (anchor - i);
        var trusted = fixedHere.Contains(anchor) ||
                      further >= 0 && further < annual.Count && Math.Abs(annual[further].FiscalYear - n.FiscalYear) == 1 && Near(annual[further].Revenue, n.Revenue, 3);
        if (!trusted) return null;
        foreach (var k in new[] { 1000m, 1_000_000m, 1 / 1000m, 1 / 1_000_000m })
        {
            if (n.Revenue / (a.Revenue * k) is not (> 0.6m and < 1.7m)) continue;
            if (a.NetIncome != 0 && !Near(a.NetIncome * k, n.NetIncome, 10)) return null;
            // Only a run reaching the end of the figures: every year beyond this one must be out the same way (a single year
            // out of line between two good ones is the neighbours test's to judge).
            for (var j = beyond; j >= 0 && j < annual.Count; j += beyond - i)
                if (!Near(annual[j].Revenue, a.Revenue, 10)) return null;
            return k;
        }
        return null;
    }

    private static FinancialPeriod Scale(FinancialPeriod f, decimal k) => f with { Revenue = f.Revenue * k, NetIncome = f.NetIncome * k };

    /// <summary>True when two figures are within a factor of each other in size (either sign).</summary>
    private static bool Near(decimal a, decimal b, decimal factor) =>
        a != 0 && b != 0 && Math.Abs(a / b) is var r && r > 1 / factor && r < factor;

    /// <summary>True when a year's figure and its quarters' estimate are within a factor of each other (same sign).</summary>
    private static bool Agrees(decimal value, decimal? estimate, decimal factor) =>
        estimate is { } e && e != 0 && value != 0 && value / e is var r && r > 1 / factor && r < factor;

    /// <summary>Four times the average of the year's reported first three quarters (the fourth may be worked out from the year).</summary>
    private static decimal? FromQuarters(List<FinancialPeriod> q, Func<FinancialPeriod, decimal> get)
    {
        var first3 = q.Where(f => f.FiscalQuarter is >= 1 and <= 3).ToList();
        // Only when the quarters agree in sign: quarters near zero either way give no scale to judge by.
        return first3.Count >= 2 && first3.Select(get).All(v => v > 0) || first3.Count >= 2 && first3.Select(get).All(v => v < 0) ? first3.Average(get) * 4 : null;
    }

    private static (decimal Prev, decimal Next)? Neighbours(List<FinancialPeriod> annual, int i, Func<FinancialPeriod, decimal> get) =>
        i > 0 && i < annual.Count - 1 && annual[i - 1].FiscalYear == annual[i].FiscalYear - 1 && annual[i + 1].FiscalYear == annual[i].FiscalYear + 1
            ? (get(annual[i - 1]), get(annual[i + 1]))
            : null;
}
