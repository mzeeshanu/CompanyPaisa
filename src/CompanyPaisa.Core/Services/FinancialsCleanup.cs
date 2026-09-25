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
                var income = Fix(a.NetIncome, Agrees(revenue, revenueEstimate, 2) ? incomeEstimate : null, Neighbours(annual, i, f => f.NetIncome), revenue);
                if (income != a.NetIncome) income = Sign(income, revenue, q);
                if (revenue == a.Revenue && income == a.NetIncome) continue;
                annual[i] = a with { Revenue = revenue, NetIncome = income };
                rescaled++;
            }

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
    private static decimal Fix(decimal value, decimal? quarterEstimate, (decimal Prev, decimal Next)? neighbours, decimal? revenue)
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
        if (revenue is { } rev && rev >= 10_000_000 && Math.Abs(value) > rev * 100 && Math.Abs(value) / 1000 < rev * 10)
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

    private static (decimal, decimal)? Neighbours(List<FinancialPeriod> annual, int i, Func<FinancialPeriod, decimal> get) =>
        i > 0 && i < annual.Count - 1 && annual[i - 1].FiscalYear == annual[i].FiscalYear - 1 && annual[i + 1].FiscalYear == annual[i].FiscalYear + 1
            ? (get(annual[i - 1]), get(annual[i + 1]))
            : null;
}
