using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

/// <summary>Cases from real European filings (November 2026).</summary>
public class EsefReconcilerTests
{
    private static EsefYear Year(int year, decimal revenue, decimal profit, decimal? revenueAndIncome = null)
    {
        var candidates = new Dictionary<string, decimal> { ["ifrs-full:Revenue"] = revenue };
        if (revenueAndIncome is { } both) candidates["ifrs-full:RevenueAndOperatingIncome"] = both;
        return new EsefYear(year, new DateOnly(year, 12, 31), revenue, profit, null, null, "EUR", "ifrs-full:Revenue", candidates);
    }

    private static EsefReport Report(int year, params EsefYear[] years) => new(new DateOnly(year, 12, 31), $"r{year}", years);

    private static List<EsefReport> Reconcile(params EsefReport[] reports) => EsefReconciler.Reconcile(reports, "X", []);

    private static EsefYear Stated(List<EsefReport> reports, string source, int year) =>
        reports.Single(r => r.Source == source).Years.Single(y => y.FiscalYear == year);

    [Fact]
    public void An_interim_report_in_thousands_is_scaled_to_the_annual_report()
    {
        var interim = new EsefReport(new DateOnly(2026, 6, 30), "interim", [Year(2025, 2_948_100m, 265_000m)]);

        var result = Reconcile(interim, Report(2025, Year(2025, 2_948_143_000m, 265_034_000m), Year(2024, 2_921_728_000m, 260_932_000m)));

        Assert.Equal(2_948_100_000m, Stated(result, "interim", 2025).Revenue);
    }

    [Fact]
    public void A_slip_is_followed_back_through_a_chain_of_reports()
    {
        // Moury Construct: only the newest report has the units right.
        var result = Reconcile(Report(2024, Year(2024, 186_340_000m, 24_422_000m), Year(2023, 194_027_000m, 24_376_000m)),
            Report(2023, Year(2023, 194_027m, 24_376m), Year(2022, 155_351m, 17_269m)),
            Report(2022, Year(2022, 155_351m, 17_269m), Year(2021, 134_822m, 13_005m)));

        Assert.Equal(134_822_000m, Stated(result, "r2022", 2021).Revenue);
        Assert.Equal(13_005_000m, Stated(result, "r2022", 2021).NetIncome);
    }

    [Fact]
    public void Revenue_and_profit_can_slip_by_different_amounts()
    {
        // Lenzing 2020: revenue in millions, profit in thousands.
        var result = Reconcile(Report(2021, Year(2021, 2_194_624_000m, 110_346_000m), Year(2020, 1_632_607_000m, -10_595_000m)),
            Report(2020, Year(2020, 1_632m, -10_595m), Year(2019, 2_105m, 114_941m)));

        Assert.Equal(2_105_000_000m, Stated(result, "r2020", 2019).Revenue);
        Assert.Equal(114_941_000m, Stated(result, "r2020", 2019).NetIncome);
    }

    [Fact]
    public void A_restated_revenue_takes_the_slip_the_profit_proves()
    {
        // WithSecure's 2023 report: a thousand times too big, and 2023's revenue restated the year after.
        var result = Reconcile(Report(2024, Year(2024, 116_002_000m, -37_979_000m), Year(2023, 109_939_000m, -40_030_000m)),
            Report(2023, Year(2023, 142_812_000_000m, -40_030_000_000m), Year(2022, 134_700_000_000m, 430_316_000_000m)));

        Assert.Equal(134_700_000m, Stated(result, "r2023", 2022).Revenue);
    }

    [Fact]
    public void A_slip_in_the_newest_annual_report_is_repaired_from_the_one_before()
    {
        var result = Reconcile(Report(2025, Year(2025, 1_279_200m, 77_800m), Year(2024, 1_203_783m, 57_675m)),
            Report(2024, Year(2024, 1_203_783_000m, 57_675_000m), Year(2023, 1_084_126_000m, 21_522_000m)));

        Assert.Equal(1_279_200_000m, Stated(result, "r2025", 2025).Revenue);
    }

    [Fact]
    public void A_revenue_tag_on_the_wrong_line_is_left_out()
    {
        // Brunello Cucinelli's 2025 report.
        var result = Reconcile(Report(2025, Year(2025, 188_000m, 141_989_000m), Year(2024, 188_000m, 128_513_000m)),
            Report(2024, Year(2024, 1_278_540_000m, 119_478_000m), Year(2023, 1_139_420_000m, 114_617_000m)));

        Assert.Equal("r2024", Assert.Single(result).Source);
    }

    [Fact]
    public void A_revenue_bigger_than_revenue_and_income_together_is_left_out()
    {
        var result = Reconcile(Report(2024, Year(2024, 134_000m, -47_456_000m, 1_097_000m), Year(2023, 595_603_000m, 10_648_000m, 57_092_000m)),
            Report(2022, Year(2022, 370_000m, -53_785_000m), Year(2021, 599_000m, -24_935_000m)));

        Assert.Equal(2024, Assert.Single(result.Single(r => r.Source == "r2024").Years).FiscalYear);
    }

    [Fact]
    public void Other_income_tagged_as_revenue_and_income_does_not_cost_a_company_its_revenue()
    {
        // GVS tags €8.5M of other income as "revenue and operating income".
        var result = Reconcile(Report(2025, Year(2025, 433_189_000m, 18_431_000m, 8_527_000m), Year(2024, 436_357_000m, 33_370_000m, 7_815_000m)));

        Assert.Equal(2, Assert.Single(result).Years.Count);
    }

    [Fact]
    public void One_mistyped_profit_is_outvoted_and_not_spread_to_older_years()
    {
        // Park Street's 2023 report mistyped 2022's profit; two other reports have it right.
        var interim = new EsefReport(new DateOnly(2023, 6, 30), "interim", [Year(2022, 153_281_000m, 54_981_000m)]);

        var result = Reconcile(Report(2023, Year(2023, 166_142_000m, -24_245_000m), Year(2022, 153_281_000m, 54_980m)), interim,
            Report(2022, Year(2022, 153_281_000m, 54_980_000m), Year(2021, 158_264_000m, 145_458_000m)));

        Assert.Equal(54_981_000m, Stated(result, "r2023", 2022).NetIncome);
        Assert.Equal(145_458_000m, Stated(result, "r2022", 2021).NetIncome);
    }

    [Fact]
    public void An_outlier_nothing_can_prove_is_left_out_not_guessed()
    {
        // Digitalist: 2020–21 in thousands, and no 2022 report to overlap with.
        var result = Reconcile(Report(2023, Year(2023, 16_681_000m, -4_042_000m), Year(2022, 18_563_000m, -6_533_000m)),
            Report(2021, Year(2021, 18_482m, -5_797m), Year(2020, 20_487m, -11_820m)));

        Assert.Equal("r2023", Assert.Single(result).Source);
    }

    [Fact]
    public void A_real_restatement_is_untouched()
    {
        // mySafety: restated to the continuing business, 18 times smaller.
        var older = Report(2021, Year(2021, 16_398_000m, 9_439_000m));

        var result = Reconcile(Report(2022, Year(2022, 889_000m, -22_527_000m), Year(2021, 886_000m, 9_439_000m)), older);

        Assert.Equal(16_398_000m, Stated(result, "r2021", 2021).Revenue);
    }

    [Fact]
    public void Trailing_twelve_months_in_an_interim_report_are_not_a_fiscal_year()
    {
        // DFDS, third quarter 2026: nine months, the last twelve months and the last full year.
        var json = $$"""
            { "facts": {
              "a": {{Fact("Revenue", "2025-10-01", "2026-10-01", 32_066)}}, "b": {{Fact("ProfitLoss", "2025-10-01", "2026-10-01", 145)}},
              "c": {{Fact("Revenue", "2026-01-01", "2026-10-01", 24_763)}}, "d": {{Fact("ProfitLoss", "2026-01-01", "2026-10-01", 430)}},
              "e": {{Fact("Revenue", "2025-01-01", "2026-01-01", 30_947)}}, "f": {{Fact("ProfitLoss", "2025-01-01", "2026-01-01", -425)}} } }
            """;

        var year = Assert.Single(EsefFinancials.Extract(json, new DateOnly(2026, 9, 30)));

        Assert.Equal((2025, 30_947m), (year.FiscalYear, year.Revenue));
    }

    private static string Fact(string concept, string start, string endExclusive, int value) =>
        $$"""{ "value": "{{value}}", "dimensions": { "concept": "ifrs-full:{{concept}}", "period": "{{start}}T00:00:00/{{endExclusive}}T00:00:00", "unit": "iso4217:DKK" } }""";
}
