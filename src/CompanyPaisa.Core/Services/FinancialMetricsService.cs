using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// Turns raw periods into headline indicators. Thresholds come from <see cref="MetricsOptions"/>.
/// TTM is the last four quarters only when they are consecutive and no older than the latest fiscal year; otherwise it is
/// that fiscal year, labelled as one, unless it is over <see cref="MaxYearsBehind"/> years older than the quarters.
/// </summary>
public sealed class FinancialMetricsService(IOptionsMonitor<MetricsOptions> options) : IFinancialMetricsService
{
    /// <summary>An older fiscal year would pass for today's size (the importer drops companies on the same limit).</summary>
    private const int MaxYearsBehind = 2;

    public CompanyIndicators Compute(IReadOnlyList<FinancialPeriod> periods, string? fiscalYearEnd = null)
    {
        var o = options.CurrentValue;
        var quarters = Ordered(periods, PeriodType.Quarterly);
        var years = Ordered(periods, PeriodType.Annual);

        decimal ttmRevenue = 0, ttmNet = 0;
        decimal? priorRevenue = null;
        int? totalsYear = null;
        var hasTotals = true;
        if (IsRun(quarters, 4) && (years.Count == 0 || !IsOlder(quarters[^1], years[^1], fiscalYearEnd)))
        {
            ttmRevenue = quarters.TakeLast(4).Sum(p => p.Revenue);
            ttmNet = quarters.TakeLast(4).Sum(p => p.NetIncome);
            if (IsRun(quarters, 8)) priorRevenue = quarters.SkipLast(4).TakeLast(4).Sum(p => p.Revenue);
        }
        else if (years.Count > 0 && (quarters.Count == 0 || years[^1].FiscalYear >= quarters[^1].FiscalYear - MaxYearsBehind))
        {
            ttmRevenue = years[^1].Revenue;
            ttmNet = years[^1].NetIncome;
            totalsYear = years[^1].FiscalYear;
            if (years.Count >= 2 && years[^2].FiscalYear == totalsYear - 1) priorRevenue = years[^2].Revenue;
        }
        else if (periods.Count > 0)
        {
            hasTotals = false;
        }
        else
        {
            return CompanyIndicators.Empty(o.CagrYears);
        }

        var growth = Ratio(ttmRevenue, priorRevenue) is { } r ? r - 1 : (decimal?)null;
        var margin = ttmRevenue != 0 ? Round(ttmNet / ttmRevenue) : (decimal?)null;

        decimal? cagr = null;
        if (years.Count > 0)
        {
            var latest = years[^1];
            var start = years.FirstOrDefault(y => y.FiscalYear == latest.FiscalYear - o.CagrYears);
            if (start is { Revenue: > 0 } && latest.Revenue > 0)
                cagr = Round((decimal)(Math.Pow((double)(latest.Revenue / start.Revenue), 1.0 / o.CagrYears) - 1));
        }

        var trend = TrendFor(growth, ttmNet, o);
        var history = years.TakeLast(o.HistoryYears).ToList();

        return new CompanyIndicators(ttmRevenue, ttmNet, growth is null ? null : Round(growth.Value), cagr, o.CagrYears,
            margin, trend, quarters.LastOrDefault(), history, totalsYear, hasTotals);
    }

    /// <summary>The newest <paramref name="count"/> quarters follow one another without a gap.</summary>
    private static bool IsRun(List<FinancialPeriod> quarters, int count) =>
        quarters.Count >= count && QuarterIndex(quarters[^1]) - QuarterIndex(quarters[^count]) == count - 1;

    private static int QuarterIndex(FinancialPeriod p) => p.FiscalYear * 4 + (p.FiscalQuarter ?? 1) - 1;

    /// <summary>
    /// The quarters stopped before the latest fiscal year ended. Quarters carry calendar labels, which can sit one quarter
    /// off an odd year-end, so inside the same year it takes two quarters to call them older.
    /// </summary>
    private static bool IsOlder(FinancialPeriod quarter, FinancialPeriod year, string? fiscalYearEnd)
    {
        if (quarter.FiscalYear < year.FiscalYear) return true;
        if (!DateOnly.TryParseExact($"2000-{fiscalYearEnd}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return false;
        // A year ending in January or February carries the year before's label.
        var endsIn = new DateOnly(year.FiscalYear + (end.Month <= 2 ? 1 : 0), end.Month, 1);
        // A year-end still to come means the company changed it, and this one doesn't describe that fiscal year.
        if (endsIn > DateOnly.FromDateTime(DateTime.UtcNow)) return false;
        var middle = endsIn.AddMonths(-1);
        return middle.Year * 4 + (middle.Month - 1) / 3 - QuarterIndex(quarter) >= 2;
    }

    public IReadOnlyList<(FinancialPeriod Period, decimal? RevenueGrowthYoY)> WithGrowth(IReadOnlyList<FinancialPeriod> periods, PeriodType type)
    {
        var ordered = Ordered(periods, type);
        var byKey = ordered.ToDictionary(p => (p.FiscalYear, p.FiscalQuarter));
        return ordered
            .Select(p => (p, byKey.TryGetValue((p.FiscalYear - 1, p.FiscalQuarter), out var prev) && Ratio(p.Revenue, prev.Revenue) is { } r
                ? Round(r - 1) : (decimal?)null))
            .ToList();
    }

    private static TrendStatus TrendFor(decimal? growth, decimal ttmNet, MetricsOptions o)
    {
        if (o.LossMakingIsDown && ttmNet < 0) return TrendStatus.Down;
        if (growth is null) return TrendStatus.Flat;
        var pct = growth.Value * 100;
        if (pct > o.TrendUpThresholdPct) return TrendStatus.Up;
        if (pct < o.TrendDownThresholdPct) return TrendStatus.Down;
        return TrendStatus.Flat;
    }

    private static List<FinancialPeriod> Ordered(IEnumerable<FinancialPeriod> periods, PeriodType type) =>
        periods.Where(p => p.PeriodType == type).OrderBy(p => p.SortKey).ToList();

    private static decimal? Ratio(decimal value, decimal? baseline) =>
        baseline is > 0 ? value / baseline.Value : null;

    private static decimal Round(decimal v) => Math.Round(v, 4);
}
