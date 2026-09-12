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
