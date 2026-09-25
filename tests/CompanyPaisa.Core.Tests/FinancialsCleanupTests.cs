using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

/// <summary>The unit-slip corrections, on figures taken from the published data.</summary>
public class FinancialsCleanupTests
{
    private static FinancialPeriod Year(string co, int year, decimal revenue, decimal income) =>
        new() { CompanyId = co, PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = income };

    private static FinancialPeriod Quarter(string co, int year, int q, decimal revenue, decimal income) =>
        new() { CompanyId = co, PeriodType = PeriodType.Quarterly, FiscalYear = year, FiscalQuarter = q, Revenue = revenue, NetIncome = income };

    private static FinancialPeriod Get(FinancialsCleanup.Result r, int year, int? quarter = null) =>
        r.Financials.Single(f => f.FiscalYear == year && f.FiscalQuarter == quarter);

    [Fact]
    public void Scales_a_profit_filed_in_thousands_up_from_its_quarters()
    {
        // Con Edison 2023: profit stored as $2,519,000 beside quarters of hundreds of millions; Q4 worked out from it.
        var r = FinancialsCleanup.Apply(
        [
            Year("ED", 2023, 14_663_000_000, 2_519_000),
            Quarter("ED", 2023, 1, 4_400_000_000, 1_004_000_000), Quarter("ED", 2023, 2, 3_300_000_000, 1_128_000_000),
            Quarter("ED", 2023, 3, 3_519_000_000, 53_000_000), Quarter("ED", 2023, 4, 3_444_000_000, 2_519_000m - 2_185_000_000m)
        ]);

        Assert.Equal(2_519_000_000, Get(r, 2023).NetIncome);
        Assert.Equal(334_000_000, Get(r, 2023, 4).NetIncome);
    }

    [Fact]
    public void Scales_revenue_filed_as_thousands_of_times_too_much_and_redoes_the_fourth_quarter()
    {
        // Tigo Energy 2024: $54.0 billion beside quarters of $10–14 million.
        var r = FinancialsCleanup.Apply(
        [
            Year("TYGO", 2024, 54_014_000_000, -62_746_000),
            Quarter("TYGO", 2024, 1, 9_802_000, -11_506_000), Quarter("TYGO", 2024, 2, 12_701_000, -11_321_000),
            Quarter("TYGO", 2024, 3, 14_237_000, -13_117_000), Quarter("TYGO", 2024, 4, 54_014_000_000m - 36_740_000m, -26_802_000)
        ]);

        Assert.Equal(54_014_000, Get(r, 2024).Revenue);
        Assert.Equal(17_274_000, Get(r, 2024, 4).Revenue);
    }

    [Fact]
    public void Turns_a_profit_into_the_loss_its_quarters_show_only_when_the_fourth_quarter_would_be_impossible()
    {
        // The RealReal 2021: "$236 billion profit" after three quarterly losses of $56–71 million.
        var real = FinancialsCleanup.Apply(
        [
            Year("REAL", 2021, 467_692_000, 236_107_000_000),
            Quarter("REAL", 2021, 1, 98_817_000, -55_993_000), Quarter("REAL", 2021, 2, 104_912_000, -70_723_000),
            Quarter("REAL", 2021, 3, 118_838_000, -57_196_000)
        ]);
        // G-III 2022: a loss (an impairment in the fourth quarter) after three profitable quarters keeps its sign.
        var giii = FinancialsCleanup.Apply(
        [
            Year("GIII", 2022, 3_226_728_000, -134_382),
            Quarter("GIII", 2022, 1, 605_000_000, 50_000_000), Quarter("GIII", 2022, 2, 606_000_000, 18_000_000),
            Quarter("GIII", 2022, 3, 1_080_000_000, 60_000_000)
        ]);

        Assert.Equal(-236_107_000, Get(real, 2021).NetIncome);
        Assert.Equal(-134_382_000, Get(giii, 2022).NetIncome);
    }

    [Fact]
    public void Scales_a_year_off_by_1000_from_both_neighbours()
    {
        // Luve 2023 (no quarters in European reports): €615,823 between €617 million and €587 million.
        var r = FinancialsCleanup.Apply([Year("LUVE.MI", 2022, 617_075_000, 47_714_000), Year("LUVE.MI", 2023, 615_823, 29_745), Year("LUVE.MI", 2024, 587_205_000, 34_497_000)]);

        Assert.Equal(615_823_000, Get(r, 2023).Revenue);
        Assert.Equal(29_745_000, Get(r, 2023).NetIncome);
    }

    [Fact]
    public void Leaves_a_year_alone_when_its_quarters_are_the_ones_in_the_wrong_unit()
    {
        // Palatin 2016: a $51.7 million loss with no revenue; its quarters were filed in thousands.
        var r = FinancialsCleanup.Apply(
        [
            Year("PTN", 2016, 0, -51_712_936),
            Quarter("PTN", 2016, 1, 0, -13_000), Quarter("PTN", 2016, 2, 0, -12_500), Quarter("PTN", 2016, 3, 0, -13_200)
        ]);

        Assert.Equal(-51_712_936, Get(r, 2016).NetIncome);
    }

    [Fact]
    public void Replaces_a_fourth_quarter_from_another_period_and_leaves_lumpy_ones()
    {
        // Carnival 2019: a "Q4" of $35 million sales and a $2.2 billion loss; the year less three quarters is $4.78 billion.
        var ccl = FinancialsCleanup.Apply(
        [
            Year("CCL", 2019, 20_825_000_000, 2_990_000_000),
            Quarter("CCL", 2019, 1, 4_673_000_000, 336_000_000), Quarter("CCL", 2019, 2, 4_838_000_000, 451_000_000),
            Quarter("CCL", 2019, 3, 6_533_000_000, 1_780_000_000), Quarter("CCL", 2019, 4, 35_000_000, -2_223_000_000)
        ]);
        // Zymeworks 2022: a $402 million upfront payment in the fourth quarter is real.
        var zyme = FinancialsCleanup.Apply(
        [
            Year("ZYME", 2022, 412_000_000, 124_000_000),
            Quarter("ZYME", 2022, 1, 3_000_000, -60_000_000), Quarter("ZYME", 2022, 2, 3_000_000, -60_000_000),
            Quarter("ZYME", 2022, 3, 3_500_000, -65_000_000), Quarter("ZYME", 2022, 4, 402_500_000, 309_000_000)
        ]);

        Assert.Equal(4_781_000_000, Get(ccl, 2019, 4).Revenue);
        Assert.Equal(423_000_000, Get(ccl, 2019, 4).NetIncome);
        Assert.Equal(402_500_000, Get(zyme, 2022, 4).Revenue);
    }

    [Fact]
    public void Leaves_ordinary_figures_alone()
    {
        var input = new[]
        {
            Year("AAPL", 2024, 391_035_000_000, 93_736_000_000), Year("AAPL", 2025, 416_161_000_000, 112_010_000_000),
            Quarter("AAPL", 2025, 1, 124_300_000_000, 36_330_000_000), Quarter("AAPL", 2025, 2, 95_359_000_000, 24_780_000_000),
            Quarter("AAPL", 2025, 3, 94_036_000_000, 23_434_000_000), Quarter("AAPL", 2025, 4, 102_466_000_000, 27_466_000_000)
        };

        var r = FinancialsCleanup.Apply(input);

        Assert.Equal(0, r.Rescaled + r.QuartersRedone + r.QuartersDropped);
        Assert.Equal(input, r.Financials.OrderBy(f => f.FiscalYear).ThenBy(f => f.FiscalQuarter ?? 0).ToArray());
    }
}
