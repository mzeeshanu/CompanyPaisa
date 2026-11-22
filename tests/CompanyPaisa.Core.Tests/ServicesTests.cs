using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class HaversineDistanceCalculatorTests
{
    private readonly HaversineDistanceCalculator _calc = new();
    private static readonly GeoPoint Lehi = new(40.3916, -111.8508);
    private static readonly GeoPoint SaltLakeCity = new(40.7608, -111.8910);

    [Fact]
    public void Lehi_to_Salt_Lake_City_is_about_25_miles() =>
        Assert.InRange(_calc.DistanceMiles(Lehi, SaltLakeCity), 24.5, 26.5);

    [Fact]
    public void Distance_to_self_is_zero() => Assert.Equal(0, _calc.DistanceMiles(Lehi, Lehi), 6);

    [Fact]
    public void Bounding_box_contains_points_inside_the_radius()
    {
        var box = _calc.BoundingBox(Lehi, 30);
        Assert.True(box.Contains(SaltLakeCity));
        Assert.False(_calc.BoundingBox(Lehi, 10).Contains(SaltLakeCity));
    }
}

public class FinancialMetricsServiceTests
{
    private static FinancialMetricsService Service(MetricsOptions? o = null) => new(Opt.Monitor(o ?? new MetricsOptions()));

    private static List<FinancialPeriod> Quarters(decimal priorQuarter, decimal latestQuarter, decimal margin) =>
        Enumerable.Range(0, 8).Select(i =>
        {
            var rev = i < 4 ? priorQuarter : latestQuarter;
            return new FinancialPeriod { CompanyId = "X", PeriodType = PeriodType.Quarterly, FiscalYear = 2024 + i / 4, FiscalQuarter = i % 4 + 1, Revenue = rev, NetIncome = rev * margin };
        }).ToList();

    [Fact]
    public void Ttm_sums_the_last_four_quarters_and_growth_compares_to_the_four_before()
    {
        var result = Service().Compute(Quarters(100, 110, 0.1m));
        Assert.Equal(440, result.TtmRevenue);
        Assert.Equal(44, result.TtmNetIncome);
        Assert.Equal(0.10m, result.RevenueGrowthYoY);
        Assert.Equal(0.1m, result.NetMargin);
        Assert.Equal("Q4 2025", result.LatestQuarter!.Label);
    }

    [Theory]
    [InlineData(100, 110, 0.1, TrendStatus.Up)]     // +10% and profitable
    [InlineData(100, 102, 0.1, TrendStatus.Flat)]   // +2% is inside the flat band
    [InlineData(100, 95, 0.1, TrendStatus.Down)]    // -5%
    [InlineData(100, 130, -0.2, TrendStatus.Down)]  // growing fast but losing money
    public void Trend_follows_configured_thresholds(double prior, double latest, double margin, TrendStatus expected) =>
        Assert.Equal(expected, Service().Compute(Quarters((decimal)prior, (decimal)latest, (decimal)margin)).Trend);

    [Fact]
    public void Thresholds_come_from_configuration()
    {
        var strict = new MetricsOptions { TrendUpThresholdPct = 15, TrendDownThresholdPct = -1 };
        Assert.Equal(TrendStatus.Flat, Service(strict).Compute(Quarters(100, 110, 0.1m)).Trend);
    }

    [Fact]
    public void Falls_back_to_annual_figures_and_computes_cagr()
    {
        var years = Enumerable.Range(2020, 6).Select(y => new FinancialPeriod
        {
            CompanyId = "X", PeriodType = PeriodType.Annual, FiscalYear = y,
            Revenue = 1000m * (decimal)Math.Pow(1.1, y - 2020), NetIncome = 50
        }).ToList();

        var result = Service().Compute(years);
        Assert.Equal(years[^1].Revenue, result.TtmRevenue);
        Assert.Equal(0.1m, result.RevenueCagr!.Value, 3);
        Assert.Equal(6, result.AnnualHistory.Count);
    }

    private static FinancialPeriod Quarter(int year, int quarter, decimal revenue) =>
        new() { CompanyId = "X", PeriodType = PeriodType.Quarterly, FiscalYear = year, FiscalQuarter = quarter, Revenue = revenue, NetIncome = 1 };

    private static FinancialPeriod Year(int year, decimal revenue) =>
        new() { CompanyId = "X", PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = 4 };

    [Fact]
    public void Quarters_with_a_gap_give_the_latest_fiscal_year()
    {
        // Brookfield Business Corp: four second quarters from four different years.
        var result = Service().Compute([Year(2024, 250), Year(2025, 275), Quarter(2023, 2, 110), Quarter(2024, 2, 120), Quarter(2025, 2, 115), Quarter(2026, 2, 123)]);
        Assert.Equal((275m, (int?)2025, true), (result.TtmRevenue, result.TotalsYear, result.HasTotals));
        Assert.Equal(0.1m, result.RevenueGrowthYoY);
    }

    [Fact]
    public void Four_consecutive_quarters_are_the_last_12_months()
    {
        var result = Service().Compute([Year(2025, 400), Quarter(2025, 3, 100), Quarter(2025, 4, 100), Quarter(2026, 1, 110), Quarter(2026, 2, 120)], "12-31");
        Assert.Equal((430m, (int?)null, true), (result.TtmRevenue, result.TotalsYear, result.HasTotals));
    }

    [Fact]
    public void A_gap_before_the_last_12_months_leaves_growth_unknown()
    {
        var quarters = Quarters(100, 110, 0.1m);
        quarters[0] = quarters[0] with { FiscalYear = 2020 };
        var result = Service().Compute(quarters);
        Assert.Equal((440m, (decimal?)null), (result.TtmRevenue, result.RevenueGrowthYoY));
    }

    [Fact]
    public void A_missing_year_leaves_growth_unknown() =>
        Assert.Null(Service().Compute([Year(2022, 100), Year(2025, 200)]).RevenueGrowthYoY);

    [Theory]
    [InlineData(2022, 4, "12-31", 2025)]   // the quarters stopped years ago
    [InlineData(2025, 2, "12-31", 2025)]   // and half a year before the year ended
    [InlineData(2025, 2, "06-30", null)]   // a June year ends with the second calendar quarter
    [InlineData(2025, 3, "12-31", null)]   // one quarter can be the calendar label's doing
    [InlineData(2025, 2, null, null)]      // no year-end, no proof
    public void Quarters_older_than_the_latest_year_give_that_year(int year, int quarter, string? yearEnd, int? expected)
    {
        var last = year * 4 + quarter - 1;
        var periods = Enumerable.Range(last - 3, 4).Select(i => Quarter(i / 4, i % 4 + 1, 10)).Append(Year(2025, 99)).ToList();
        Assert.Equal(expected, Service().Compute(periods, yearEnd).TotalsYear);
    }

    [Theory]
    [InlineData(2009)]
    [InlineData(null)]
    public void A_gap_and_no_recent_year_give_no_total(int? annual)
    {
        List<FinancialPeriod> periods = [Quarter(2025, 1, 10), Quarter(2025, 2, 10), Quarter(2026, 1, 10), Quarter(2026, 2, 12)];
        if (annual is { } year) periods.Add(Year(year, 500));
        var result = Service().Compute(periods);
        Assert.Equal((false, 0m), (result.HasTotals, result.TtmRevenue));
        Assert.Equal("Q2 2026", result.LatestQuarter!.Label);
    }

    [Fact]
    public void No_data_gives_empty_indicators() =>
        Assert.Equal(0, Service().Compute([]).TtmRevenue);

    [Fact]
    public void Growth_per_period_compares_with_the_same_period_a_year_earlier()
    {
        var rows = Service().WithGrowth(Quarters(100, 120, 0.1m), PeriodType.Quarterly);
        Assert.Null(rows[0].RevenueGrowthYoY);
        Assert.Equal(0.2m, rows[4].RevenueGrowthYoY);
    }
}
