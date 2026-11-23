using CompanyPaisa.Importer.Compensation;

namespace CompanyPaisa.Core.Tests;

public class PayTableAsTextTests
{
    // ExxonMobil's 2026 proxy prints the table as a picture, with this text hidden behind it.
    private const string Html = """
        <p><img src="table.jpg"/></p><p style="font-size:0.5pt"><span style="color:#FFFFFF">EXECUTIVE COMPENSATION TABLES
        SUMMARY COMPENSATION TABLE FOR 2025 Change in Pension Value and Nonqualified Non-Equity Deferred Stock Option Incentive
        Plan Compensation All Other Name and Salary Bonus Awards Awards Compensation Earnings Compensation Total Principal
        Position Year ($)1 ($)2 ($)3 ($) ($) ($)4 ($)5 ($) D.W. Woods 2025 2,058,000 4,093,000 25,886,250 0 0 0 961,378
        32,998,628 Chairman and CEO 2024 1,969,000 4,548,000 26,721,000 0 0 10,417,644 396,893 44,052,537 2023 1,875,000
        4,787,000 23,199,750 0 0 5,922,953 1,135,195 36,919,898 J.P. Williams, Jr. 2025 1,100,000 2,000,000 9,000,000 0 0 0
        107,439 12,207,439 Senior Vice President 2024 1,000,000 1,900,000 8,000,000 0 0 0 100,000 11,000,001 (1) Salary
        </span></p>
        """;

    [Fact]
    public void A_table_given_as_hidden_text_is_read_and_checked()
    {
        var rows = new SummaryCompensationTableParser().Parse(Html).Rows;
        Assert.Equal(5, rows.Count);
        var woods = rows.Single(r => r is { Name: "D.W. Woods", Year: 2024 });
        Assert.Equal("Chairman and CEO", woods.Title);
        Assert.Equal(44_052_537m, woods.Total);
        Assert.Equal(26_721_000m, woods.StockAwards);
        Assert.Contains(rows, r => r is { Name: "J.P. Williams, Jr.", Year: 2025, Title: "Senior Vice President" });
    }

    [Fact]
    public void A_row_that_does_not_add_up_is_left_out()
    {
        var rows = new SummaryCompensationTableParser().Parse(Html.Replace("36,919,898", "39,919,898")).Rows;
        Assert.DoesNotContain(rows, r => r is { Name: "D.W. Woods", Year: 2023 });
    }
}
