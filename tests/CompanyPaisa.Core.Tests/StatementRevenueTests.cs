using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

/// <summary>The rules that decide a year's revenue between company facts and the annual report's statement (EdgarTools).</summary>
public class StatementRevenueTests
{
    private static string Fact(string frame, decimal value, string form = "10-K", string accession = "A-26-1", string filed = "2026-02-20")
    {
        var framed = frame.Length > 0 ? $", \"frame\": \"{frame}\"" : "";
        return $$"""{ "start": "2025-01-01", "end": "2025-12-31", "val": {{value}}, "accn": "{{accession}}", "form": "{{form}}", "filed": "{{filed}}"{{framed}} }""";
    }

    private static string UsGaap(params (string Concept, string Facts)[] concepts) =>
        "{ \"facts\": { \"us-gaap\": {"
        + string.Join(",", concepts.Select(c => "\"" + c.Concept + "\": { \"units\": { \"USD\": [" + c.Facts + "] } }"))
        + "} } }";

    private static StatementReading Statement(decimal revenue, decimal profit, string label = "Net revenue", decimal? total = null) =>
        new("A-26-1", "2026-02-20", [new StatementYear(new DateOnly(2025, 12, 31), revenue, profit, label, "Revenues", total)]);

    private static decimal Revenue2025(string json, StatementReading? statement = null) =>
        Assert.Single(new XbrlFinancialsExtractor().Extract("X", 1, json, 10, statement).Periods).Revenue;

    [Fact]
    public void A_bank_gets_net_interest_income_plus_non_interest_income()
    {
        // Bank of America 2025: $113.1bn "total revenue, net of interest expense", not $191.6bn gross.
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 138_566_000_000m)), ("NoninterestIncome", Fact("CY2025", 53_001_000_000m)),
            ("InterestIncomeExpenseNet", Fact("CY2025", 60_096_000_000m)), ("Revenues", Fact("CY2025", 113_097_000_000m)), ("NetIncomeLoss", Fact("CY2025", 30_000_000_000m)));

        Assert.Equal(113_097_000_000m, Revenue2025(json));
    }

    [Fact]
    public void A_bank_without_a_net_interest_tag_gets_income_less_interest_expense()
    {
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 3_232m)), ("NoninterestIncome", Fact("CY2025", 262m)),
            ("InterestExpense", Fact("CY2025", 1_468m)), ("NetIncomeLoss", Fact("CY2025", 500m)));

        Assert.Equal(2_026m, Revenue2025(json));
    }

    [Fact]
    public void A_mistyped_sign_on_a_banks_net_interest_income_is_not_believed()
    {
        // Peoples Bancorp 2018, as restated in its 2020 10-K.
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 151_264_000m)), ("NoninterestIncome", Fact("CY2025", 2_650_000m)),
            ("InterestExpense", Fact("CY2025", 21_652_000m)), ("InterestIncomeExpenseNet", Fact("CY2025", -129_612_000m)), ("NetIncomeLoss", Fact("CY2025", 46_255_000m)));

        Assert.Equal(132_262_000m, Revenue2025(json));
    }

    [Fact]
    public void EdgarTools_never_sets_a_banks_revenue()
    {
        // First Citizens: EdgarTools read $1.4bn where the bank's total is $9.5bn.
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 11_778m)), ("NoninterestIncome", Fact("CY2025", 2_727m)),
            ("InterestIncomeExpenseNet", Fact("CY2025", 6_814m)), ("Revenues", Fact("CY2025", 1_425m)), ("NetIncomeLoss", Fact("CY2025", 2_000m)));

        Assert.Equal(9_541m, Revenue2025(json, Statement(1_425m, 2_000m, "Revenues")));
    }

    [Fact]
    public void A_year_mistagged_in_a_later_quarterly_report_does_not_replace_the_annual_report()
    {
        // Comfort Systems: the Q1 2026 10-Q tagged $1.8bn as the whole of 2025; the 10-K says $9.1bn.
        var json = UsGaap(("Revenues", Fact("", 9_101_641_000m) + "," + Fact("CY2025", 1_831_286_000m, "10-Q", "Q-26-1", "2026-04-23")),
            ("NetIncomeLoss", Fact("CY2025", 1_000_000_000m)));

        Assert.Equal(9_101_641_000m, Revenue2025(json));
    }

    private static string Tigo() => UsGaap(("Revenues", Fact("CY2025", 103_536_000m)),
        ("RevenueFromContractWithCustomerExcludingAssessedTax", Fact("CY2025", 103_536_000_000m)), ("NetIncomeLoss", Fact("CY2025", -1_880_000m)));

    [Fact]
    public void The_income_statement_settles_a_line_tagged_a_thousand_times_too_big() =>
        Assert.Equal(103_536_000m, Revenue2025(Tigo(), Statement(103_536_000m, -1_880_000m)));

    [Theory]
    [InlineData(-5_000_000, "Net revenue", null)]               // net income differs: another period, currency or unit
    [InlineData(-1_880_000, "License and other revenue", null)] // a sub-line
    [InlineData(-1_880_000, "Revenue", 900_000_000)]            // the statement prints a larger total
    public void A_statement_reading_that_cannot_be_proved_is_ignored(int profit, string label, int? total) =>
        Assert.Equal(103_536_000_000m, Revenue2025(Tigo(), Statement(103_536_000m, profit, label, total)));

    [Fact]
    public void A_statement_figure_the_company_facts_do_not_hold_is_ignored() =>
        Assert.Equal(103_536_000_000m, Revenue2025(Tigo(), Statement(99_000_000m, -1_880_000m)));

    [Fact]
    public void A_restatement_filed_after_the_report_stands()
    {
        var json = UsGaap(("Revenues", Fact("", 5_448m) + "," + Fact("CY2025", 4_354m, "10-KT", "B-26-9", "2026-09-19")),
            ("NetIncomeLoss", Fact("CY2025", 300m)));

        Assert.Equal(4_354m, Revenue2025(json, Statement(5_448m, 300m)));
    }

    [Fact]
    public void An_amendment_that_repeats_the_reports_figures_does_not_block_its_top_line()
    {
        // B. Riley 2024: the 10-K's total revenues $746.4M, repeated in the 10-K/A filed the next day, which also tags the
        // $986.0M of contract revenue that is larger only because of investment losses.
        var json = UsGaap(
            ("Revenues", Fact("", 746_421_000m) + "," + Fact("CY2025", 746_421_000m, "10-K/A", "A-26-2", "2026-02-21")),
            ("RevenueFromContractWithCustomerExcludingAssessedTax", Fact("", 985_980_000m) + "," + Fact("CY2025", 985_980_000m, "10-K/A", "A-26-2", "2026-02-21")),
            ("NetIncomeLoss", Fact("CY2025", -764_274_000m)));

        Assert.Equal(746_421_000m, Revenue2025(json, Statement(746_421_000m, -764_274_000m, "Total revenues", 746_421_000m)));
    }

    [Fact]
    public void A_banks_lease_income_is_never_its_revenue()
    {
        // Pathfinder Bancorp 2025: lease income tagged as $372M ($372 thousand); its revenue is $44.3M + $2.5M.
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 76_700_000m)), ("NoninterestIncome", Fact("CY2025", 2_530_000m)),
            ("InterestIncomeExpenseNet", Fact("CY2025", 44_300_000m)), ("OperatingLeaseLeaseIncome", Fact("CY2025", 372_000_000m)),
            ("NetIncomeLoss", Fact("CY2025", -1_933_000m)));

        Assert.Equal(46_830_000m, Revenue2025(json));
    }

    [Fact]
    public void A_bank_that_tags_deposit_interest_as_non_operating_still_gets_net_interest_income()
    {
        // NWPP 2025: interest income $48.6M, interest paid $15.4M, non-interest income $9.9M.
        var json = UsGaap(("InterestAndDividendIncomeOperating", Fact("CY2025", 48_597_000m)), ("NoninterestIncome", Fact("CY2025", 9_900_000m)),
            ("InterestExpenseNonoperating", Fact("CY2025", 15_431_000m)), ("NetIncomeLoss", Fact("CY2025", 10_098_000m)));

        Assert.Equal(43_066_000m, Revenue2025(json));
    }
}
