using CompanyPaisa.Importer.Anz;
using CompanyPaisa.Importer.Pk;

namespace CompanyPaisa.Core.Tests;

/// <summary>
/// The Australian / New Zealand importer's rules: report links on company websites, and revenue and profit from annual
/// reports — on layouts taken from real 2026 reports (as PdfLines gives them).
/// </summary>
public class AnzReportTests
{
    private static List<PdfTextLine> Lines(string text) => text.Split('\n').Select(l => PdfTextLine.Plain(l.TrimEnd('\r'))).ToList();

    [Fact]
    public void Reads_a_statement_in_us_dollar_millions()
    {
        // Brambles: two-line heading, "Note  US$m  US$m", note numbers after labels, owners' profit on one line.
        var r = ResultsReader.Read(Lines("""
            Consolidated Statement of Comprehensive
            Income
            for the year ended 30 June 2026
            2026  2025
            Note  US$m  US$m
            Continuing operations
            Sales revenue  2  7,042.9  6,669.7
            Other income and other revenue  299.3  257.5
            Operating expenses  3  (5,843.8)  (5,550.6)
            Operating profit  1,494.4  1,371.8
            Profit before tax  1,350.8  1,234.1
            Tax expense  5A  (402.3)  (369.9)
            Profit from continuing operations  948.5  864.2
            Profit for the year attributable to members of the parent entity  954.0  896.0
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal(2026, r.FiscalYear);
        Assert.Equal(new DateOnly(2026, 6, 30), r.PeriodEnd);
        Assert.Equal("USD", r.Currency);
        Assert.Equal(7_042_900_000m, r.Revenue);
        Assert.Equal(6_669_700_000m, r.PriorRevenue);
        Assert.Equal(954_000_000m, r.NetIncome);
        Assert.Equal(896_000_000m, r.PriorNetIncome);
    }

    [Fact]
    public void Takes_the_unlabelled_total_under_an_operating_revenue_heading()
    {
        // Air New Zealand: revenue items with the total printed without a label (after a note number).
        var r = ResultsReader.Read(Lines("""
            Consolidated Statement of Financial Performance
            For the year ended 30 June
            NOTES  2026  2025
            $M  $M
            Operating revenue
            Passenger revenue  6,129  5,851
            Cargo  484  487
            Contract services  66  61
            Other revenue and income  1  337  356
            1  7,016  6,755
            Operating expenditure
            Labour  (1,739)  (1,707)
            (Loss)/Earnings before taxation  (336)  164
            Taxation credit/(expense)  3  94  (56)
            Net (loss)/profit attributable to shareholders of parent company  (242)  108
            """), "NZD");

        Assert.NotNull(r);
        Assert.Equal("NZD", r.Currency);
        Assert.Equal(7_016_000_000m, r.Revenue);
        Assert.Equal(-242_000_000m, r.NetIncome);
        Assert.Equal(108_000_000m, r.PriorNetIncome);
    }

    [Fact]
    public void Counts_only_revenue_lines_under_revenue_and_other_income()
    {
        // Worley: "$’M" (curly apostrophe), and a heading that mixes revenue with other income and interest.
        var r = ResultsReader.Read(Lines("""
            Consolidated statement of financial performance and
            other comprehensive income
            FOR THE FINANCIAL YEAR ENDED 30 JUNE 2026
            Consolidated
            2026  2025
            Notes  $’M  $’M
            REVENUE AND OTHER INCOME
            Professional services revenue  5,650  6,565
            Construction and fabrication revenue  1,988  1,834
            Procurement revenue  3,042  2,823
            Other income  18  6
            Interest income  16  11
            Total revenue and other income  4  10,714  11,239
            EXPENSES
            Professional services costs  (5,329)  (5,890)
            Profit before income tax expense  331  576
            Income tax expense  (93)  (167)
            Profit after income tax expense  238  409
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal(10_680_000_000m, r.Revenue);
        Assert.Equal(11_222_000_000m, r.PriorRevenue);
        Assert.Equal(238_000_000m, r.NetIncome);
    }

    [Fact]
    public void Takes_the_owners_line_under_attributable_to()
    {
        // Energy Resources of Australia: "$'000", a loss, and "Loss is attributable to:" then the owners' line.
        var r = ResultsReader.Read(Lines("""
            STATEMENT OF COMPREHENSIVE INCOME
            FOR THE YEAR ENDED 31 DECEMBER 2025
            2025  2024
            NOTES  $'000  $'000
            Revenue from continuing operations  3  58,871  37,196
            Materials and consumables used  (490)  (469)
            Employee benefits and contractor expenses  (5,140)  (7,577)
            Loss before income tax  (50,320)  (245,975)
            Income tax (expense)/benefit  5  -  -
            Loss for the year  (50,320)  (245,975)
            Other comprehensive loss  -  -
            Total comprehensive loss for the year  (50,320)  (245,975)
            Loss is attributable to:
            Owners of Energy Resources of Australia Ltd  (50,320)  (245,975)
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal(2025, r.FiscalYear);
        Assert.Equal(new DateOnly(2025, 12, 31), r.PeriodEnd);
        Assert.Equal(58_871_000m, r.Revenue);
        Assert.Equal(-50_320_000m, r.NetIncome);
    }

    [Fact]
    public void Falls_back_to_the_appendix_4e_summary()
    {
        // Clinuvel: the 4E table in full dollars; the statement itself isn't in the document.
        var r = ResultsReader.Read(Lines("""
            Appendix 4E
            Preliminary final report for the year ended 30 June 2026
            2.  Results for announcement to the market.  Percentage change to 2026  Amount (A$)
            2.1 Revenues from ordinary activities.  Decreased 1%  To  94,024,398
            2.2 Profit from ordinary activities before tax attributable to members.  Decreased 8%  To  47,659,765
            2.3 Net profit for the period attributable to members.  Decreased 6%  To  33,916,819
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal("4E", r.Source);
        Assert.Equal(2026, r.FiscalYear);
        Assert.Equal(94_024_398m, r.Revenue);
        Assert.Equal(33_916_819m, r.NetIncome);
        Assert.Null(r.PriorRevenue);
    }

    [Fact]
    public void Reads_columns_printed_oldest_year_first()
    {
        // Fisher & Paykel Healthcare: "2025  2026", NZ$M.
        var r = ResultsReader.Read(Lines("""
            CONSOLIDATED INCOME STATEMENT
            For the year ended 31 March 2026
            2025  2026
            Notes  NZ$M  NZ$M
            Operating revenue  4  2,021.0  2,308.4
            Cost of sales  (750.1)  (838.3)
            Gross profit  1,270.9  1,470.1
            Operating profit  509.6  636.4
            Profit before tax  503.3  631.5
            Tax expense  (126.1)  (163.0)
            Profit after tax  377.2  468.5
            """), "NZD");

        Assert.NotNull(r);
        Assert.Equal(2026, r.FiscalYear);
        Assert.Equal(new DateOnly(2026, 3, 31), r.PeriodEnd);
        Assert.Equal("NZD", r.Currency);
        Assert.Equal(2_308_400_000m, r.Revenue);
        Assert.Equal(2_021_000_000m, r.PriorRevenue);
        Assert.Equal(468_500_000m, r.NetIncome);
    }

    [Fact]
    public void Reads_a_bank_as_net_interest_plus_other_operating_income()
    {
        // NAB: group and company columns, sideways page-edge letters after the amounts, owners' profit on its own line.
        var r = ResultsReader.Read(Lines("""
            Income statements
            Group  Company
            For the year ended 30 September  Note  2025  2024  2025  2024
            $m  $m  $m  $m
            Interest income
            Effective interest rate method  49,870  52,012  46,290  48,036  o
            Interest expense  (39,376)  (41,540)  (39,309)  (41,056)
            Net interest income  3  17,403  16,757  13,413  12,725
            Other operating income  4  3,469  3,875  5,268  5,547
            Operating expenses  5  (10,348)  (10,012)  (9,356)  (8,807)  i  r
            Profit before income tax  9,691  9,879  8,519  8,846
            Net profit for the year  6,798  6,978  6,367  6,894  C
            Attributable to non-controlling interests  39  18  -  -
            Attributable to owners of the Company  6,759  6,960  6,367  6,894  n
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal(new DateOnly(2025, 9, 30), r.PeriodEnd);
        Assert.Equal(20_872_000_000m, r.Revenue);
        Assert.Equal(6_759_000_000m, r.NetIncome);
    }

    [Fact]
    public void Reads_the_unit_of_a_4e_table_above_its_amounts()
    {
        // Transurban: "$M" over the column, amounts written "to $3,895", profit's amount on the next line.
        var r = ResultsReader.Read(Lines("""
            Appendix 4E
            Year ended 30 June 2026
            Results for announcement to the market
            2026
            Statutory results  % change
            $M
            Revenue from ordinary activities  decrease of 1.5% to  $3,895
            Profit after tax from ordinary activities  increase of 143.5% to
            $432
            Profit after tax from ordinary activities attributable to security holders of the Group  increase of 175.2% to  $366
            """), "AUD");

        Assert.NotNull(r);
        Assert.Equal(3_895_000_000m, r.Revenue);
        Assert.Equal(366_000_000m, r.NetIncome);
    }

    [Theory]
    // Endeavour: a 4E table (this year, last year, change, % change) — the first amount, not the last.
    [InlineData("""
        For the financial year ended 28 June 2026
        Results for announcement to the market
        2026  2025
        $M  $M  $M  %
        Revenue from the sale of goods and services  12,212  12,058  154  1.3
        Profit for the year after tax  52  425  (373)  (87.8)
        """, 12_212_000_000, 52_000_000)]
    // Monadelphous: "$’000" (which looks like an amount) under the heading.
    [InlineData("""
        Current year ended  30 June 2026
        Results for announcement to the market
        $’000
        Revenue  Up  29%  to  2,796,117
        Profit after tax attributable to members  Up  52%  to  127,300
        """, 2_796_117_000, 127_300_000)]
    // Seven Group: digits split by glyph spacing after "to".
    [InlineData("""
        For the year ended 30 June 2026
        RESULTS FOR ANNOUNCEMENT TO THE MARKET
        REPORTED  $m
        Revenue from ordinary activities (continuing operations)  down  1.4%  to  1 0,589.0
        Net profit from ordinary activities after income tax attributable to members  up  34.8%  to  6 55.3
        """, 10_589_000_000, 655_300_000)]
    // Lovisa: the % change printed before the amount; "To A$’000s" in the header.
    [InlineData("""
        For the year ended 28 June 2026
        2. Results for announcement to the market
        Comparison to the prior period  Increase/  Change %  To A$’000s
        Revenue from ordinary activities  Increase  17.6%  938,763
        Profit after tax attributable to the members  Increase  10.7%  95,590
        """, 938_763_000, 95_590_000)]
    // Healius: the unit shares its line with the column years.
    [InlineData("""
        Results for announcement to the market
        For the year ended 30 June 2026
        $ CHANGE  % CHANGE
        $M  2026  2025  2026 VS 2025  2026 VS 2025
        Revenue from continuing operations  1,373.2  1,344.2  29.0  2.2%
        Loss for the year after tax  (415.6)  (151.2)  (264.4)  174.9%
        """, 1_373_200_000, -415_600_000)]
    public void Reads_4e_summary_layouts(string text, long revenue, long profit)
    {
        var r = ResultsReader.Read(Lines(text), "AUD");

        Assert.NotNull(r);
        Assert.Equal(revenue, r.Revenue);
        Assert.Equal(profit, r.NetIncome);
    }

    [Fact]
    public void Ignores_a_contents_page()
    {
        var r = ResultsReader.Read(Lines("""
            Consolidated Statement of Comprehensive Income  81
            Consolidated Balance Sheet  82
            Consolidated Cash Flow Statement  83
            """), "AUD");

        Assert.Null(r);
    }

    [Theory]
    [InlineData("2025 Annual Report", "/files/ar.pdf", 2025, 10)]
    [InlineData("Download PDF", "/wp-content/uploads/2026/08/20260827-clinuvel-appendix-4e-annual-report.pdf", 2026, 10)]
    [InlineData("", "/wp-content/uploads/2025/07/Annual-Report-2016.pdf", 2016, 10)]
    [InlineData("Annual Report 2024-25", "/x.pdf", 2025, 10)]
    [InlineData("FY26 Full year results", "/results.pdf", 2026, 6)]
    [InlineData("Appendix 4E", "/2026/apx.pdf", 2026, 8)]
    [InlineData("2026 Annual and Sustainability Report", "/r.pdf", 2026, 10)]
    public void Recognises_report_links(string text, string path, int year, int score)
    {
        var link = ReportFinder.ReportScore(text, new Uri("https://example.com.au" + path));

        Assert.NotNull(link);
        Assert.Equal(year, link.Year);
        Assert.Equal(score, link.Score);
    }

    [Theory]
    [InlineData("Half Year Report 2026", "/hy.pdf")]
    [InlineData("2026 Sustainability Report", "/s.pdf")]
    [InlineData("Appendix 4D", "/4d.pdf")]
    [InlineData("FY26 Results Presentation", "/p.pdf")]
    [InlineData("Corporate Governance Statement 2026", "/cg.pdf")]
    [InlineData("Notice of Annual General Meeting 2026", "/n.pdf")]
    public void Skips_other_documents(string text, string path) =>
        Assert.Null(ReportFinder.ReportScore(text, new Uri("https://example.com.au" + path)));

    [Fact]
    public void Reads_the_principal_office_not_the_share_registry()
    {
        var office = OfficeAddress.Find(Lines("""
            Corporate directory
            Share registry
            Computershare Investor Services
            Yarra Falls, 452 Johnston Street, Abbotsford VIC 3067
            Principal place of business
            Level 10, 25 Grenfell Street
            Adelaide SA 5000
            """), "AU");

        Assert.NotNull(office);
        Assert.Equal("5000", office.Postcode);
        Assert.Equal("Level 10, 25 Grenfell Street", office.Street);
    }

    [Fact]
    public void Starts_the_street_at_the_address_itself()
    {
        var office = OfficeAddress.Find(Lines("""
            Registered office
            Shareholder enquiries, 1 Woolworths Way, Bella Vista NSW 2153
            """), "AU");

        Assert.NotNull(office);
        Assert.Equal("1 Woolworths Way, Bella Vista", office.Street);
        Assert.Equal("2153", office.Postcode);
    }

    [Theory]
    [InlineData("BHP Group Limited", "bhp")]
    [InlineData("Fletcher Building Ltd", "fletcherbuilding")]
    [InlineData("The Star Entertainment Group Limited", "starentertainment")]
    public void Name_keys_drop_corporate_words(string name, string key) => Assert.Equal(key, AnzImportPipeline.NameKey(name));
}
