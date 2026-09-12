using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// Turns raw periods into headline indicators. Thresholds come from <see cref="MetricsOptions"/>.
/// TTM uses the last four quarters when available, otherwise the latest fiscal year.
/// </summary>
public sealed class FinancialMetricsService(IOptionsMonitor<MetricsOptions> options) : IFinancialMetricsService
{
    public CompanyIndicators Compute(IReadOnlyList<FinancialPeriod> periods)
    {
        var o = options.CurrentValue;
        var quarters = Ordered(periods, PeriodType.Quarterly);
        var years = Ordered(periods, PeriodType.Annual);

        decimal ttmRevenue, ttmNet;
        decimal? priorRevenue = null;
        if (quarters.Count >= 4)
        {
            ttmRevenue = quarters.TakeLast(4).Sum(p => p.Revenue);
            ttmNet = quarters.TakeLast(4).Sum(p => p.NetIncome);
            if (quarters.Count >= 8) priorRevenue = quarters.SkipLast(4).TakeLast(4).Sum(p => p.Revenue);
        }
        else if (years.Count > 0)
        {
            ttmRevenue = years[^1].Revenue;
            ttmNet = years[^1].NetIncome;
            if (years.Count >= 2) priorRevenue = years[^2].Revenue;
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
            margin, trend, quarters.LastOrDefault(), history);
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
