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

    [Fact]
    public void A_year_filed_in_thousands_scales_its_profit_with_its_revenue()
    {
        // Netcompany 2022: DKK 5,544,600 of revenue and 603,400 of profit, both in thousands.
        var r = FinancialsCleanup.Apply(
        [
            Year("NETC.CO", 2021, 3_631_971_000, 576_142_000), Year("NETC.CO", 2022, 5_544_600, 603_400),
            Year("NETC.CO", 2023, 6_078_400_000, 303_500_000)
        ]);

        Assert.Equal(5_544_600_000, Get(r, 2022).Revenue);
        Assert.Equal(603_400_000, Get(r, 2022).NetIncome);
    }

    [Fact]
    public void Leaves_a_real_profit_alone_when_the_revenue_line_is_what_is_small()
    {
        // Solstad Offshore: a NOK 1.1 billion loss on NOK 10 million of "revenue", beside another NOK 1.1 billion loss.
        var input = new[]
        {
            Year("SOFF.OL", 2020, 6_373_000, 7_240_743_000), Year("SOFF.OL", 2021, 10_295_000, -1_102_449_000),
            Year("SOFF.OL", 2022, 46_436_000, -1_117_803_000)
        };

        Assert.Equal(-1_102_449_000, Get(FinancialsCleanup.Apply(input), 2021).NetIncome);
    }

    [Fact]
    public void Scales_a_run_of_profits_filed_in_thousands()
    {
        // OPKO Health: every loss from 2021 to 2025 filed 1,000× too big, some only 17–75× its revenue.
        var r = FinancialsCleanup.Apply(
        [
            Year("OPK", 2020, 1_435_413_000, 30_586_000), Year("OPK", 2021, 1_774_718_000, -30_143_000_000),
            Year("OPK", 2022, 1_004_196_000, -328_405_000_000), Year("OPK", 2023, 863_495_000, -188_863_000_000),
            Year("OPK", 2024, 713_142_000, -53_224_000_000), Year("OPK", 2025, 606_879_000, -225_680_000_000)
        ]);

        Assert.Equal([30_586_000m, -30_143_000m, -328_405_000m, -188_863_000m, -53_224_000m, -225_680_000m],
            r.Financials.OrderBy(f => f.FiscalYear).Select(f => f.NetIncome));
    }

    [Fact]
    public void Leaves_a_one_off_loss_far_above_revenue_alone()
    {
        // Vivid Seats 2020: a $774 million loss on $23 million of sales in the pandemic year — real.
        var input = new[]
        {
            Year("SEAT", 2019, 403_645_000, -53_848_000), Year("SEAT", 2020, 23_281_000, -774_185_000),
            Year("SEAT", 2021, 389_668_000, -5_024_000)
        };

        Assert.Equal(0, FinancialsCleanup.Apply(input).Rescaled);
    }

    [Fact]
    public void Scales_years_at_the_ends_of_the_figures()
    {
        // Moury Construct's first three years in thousands; Scandinavian Tobacco's last year in thousands.
        var r = FinancialsCleanup.Apply(
        [
            Year("MOUR.BR", 2020, 128_601, 9_092), Year("MOUR.BR", 2021, 134_822, 13_005), Year("MOUR.BR", 2022, 155_351, 17_269),
            Year("MOUR.BR", 2023, 194_000_000, 24_400_000), Year("MOUR.BR", 2024, 186_300_000, 24_400_000),
            Year("SPG.CO", 2023, 2_610_000_000, 158_500_000), Year("SPG.CO", 2024, 2_920_000_000, 260_900_000), Year("SPG.CO", 2025, 2_948_100, 265_000)
        ]);

        Assert.Equal([128_601_000m, 134_822_000m, 155_351_000m],
            r.Financials.Where(f => f.CompanyId == "MOUR.BR" && f.FiscalYear < 2023).OrderBy(f => f.FiscalYear).Select(f => f.Revenue));
        var spg = r.Financials.Single(f => f.CompanyId == "SPG.CO" && f.FiscalYear == 2025);
        Assert.Equal((2_948_100_000m, 265_000_000m), (spg.Revenue, spg.NetIncome));
    }

    [Fact]
    public void Leaves_a_young_company_s_tiny_first_sales_alone()
    {
        // First-year sales of $20,000 with a loss on the company's own scale, then real growth.
        var input = new[] { Year("NEW", 2023, 20_000, -6_000_000), Year("NEW", 2024, 18_500_000, -5_800_000), Year("NEW", 2025, 18_600_000, -6_500_000) };

        Assert.Equal(0, FinancialsCleanup.Apply(input).Rescaled);
    }
}
