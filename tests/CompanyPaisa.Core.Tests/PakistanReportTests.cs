using CompanyPaisa.Importer.Pk;

namespace CompanyPaisa.Core.Tests;

/// <summary>The rules-based reader of Pakistani annual reports, on layouts taken from real reports (as PdfLines gives them).</summary>
public class PakistanReportTests
{
    private static PkAnnualReport Read(string text) => AnnualReportReader.Read(text.Split('\n').Select(l => l.TrimEnd('\r')).ToList());

    [Fact]
    public void Reads_revenue_and_profit_from_the_statement_of_profit_or_loss()
    {
        // Lucky Cement 2026: two-line heading, unit under the years, gross revenue less taxes, then net revenue.
        var report = Read("""
            Unconsolidated Statement of
            Profit or Loss
            For the year ended June 30, 2026
            Note  2026  2025
            (PKR in `000')
            Gross Revenue  27  192,861,184  174,302,064
            Less: Sales tax and federal excise duty  53,227,737  47,513,376
            Rebates and incentives  3,106,430  2,276,944
            56,334,167  49,790,320
            Net Revenue  136,527,017  124,511,744
            Cost of sales  28  (85,356,494)  (81,827,060)
            Gross profit  51,170,523  42,684,684
            Profit before taxation  60,541,979  46,993,459
            Taxation  34  (13,912,612)  (13,901,297)
            Profit after taxation  46,629,367  33,092,162
            ( PKR)
            Earnings per share - basic and diluted  35  31.83  22.59
            The annexed notes from 1 to 46 form an integral part of these unconsolidated financial statements.
            """);

        var s = Assert.Single(report.Statements);
        Assert.False(s.Consolidated);
        Assert.Equal(2026, s.FiscalYear);
        Assert.Equal(new DateOnly(2026, 6, 30), s.PeriodEnd);
        Assert.Equal(136_527_017_000m, s.Revenue);
        Assert.Equal(124_511_744_000m, s.PriorRevenue);
        Assert.Equal(46_629_367_000m, s.NetIncome);
        Assert.Equal(33_092_162_000m, s.PriorNetIncome);
        Assert.Equal(31.83m, s.Eps);
    }

    [Fact]
    public void Prefers_the_group_accounts_and_the_owners_share_of_profit()
    {
        var report = Read("""
            Statement of Profit or Loss
            For the year ended December 31, 2025
            2025  2024
            Rupees  Rupees
            Revenue from contracts with customers - net  27  44,230,607,495  38,526,983,552
            Cost of revenue  28  (32,390,603,463)  (28,843,675,290)
            Profit for the year  8,019,013,845  6,115,297,176
            The annexed notes 1 to 45 form an integral part of these financial statements.
            Consolidated Statement of Profit or Loss
            For the year ended December 31, 2025
            Note  2025  2024
            (Rupees in thousand)
            Revenue - net  80,391,885  67,473,021
            Cost of revenue  (60,000,000)  (50,000,000)
            Profit for the year  11,500,000  7,700,000
            Attributable to:
            Equity holders of the Holding Company  11,041,078  7,460,268
            Non-controlling interest  458,922  239,732
            The annexed notes 1 to 45 form an integral part of these consolidated financial statements.
            """);

        Assert.Equal(2, report.Statements.Count);
        var main = report.Main!;
        Assert.True(main.Consolidated);
        Assert.Equal(80_391_885_000m, main.Revenue);
        Assert.Equal(11_041_078_000m, main.NetIncome);
    }

    [Fact]
    public void Skips_analysis_pages_multi_year_summaries_and_dollar_translations()
    {
        var report = Read("""
            Horizontal Analysis
            Statement of Profit or Loss
            (Rupees in thousand)
            2024  24 vs 23  2023  23 vs 22
            Net sales  181,828,621  24.74  145,769,907  53.23
            Profit for the year  77,288,111  37.70  56,128,711  69.76
            CONSOLIDATED PROFIT AND LOSS ACCOUNT
            FOR THE YEAR ENDED DECEMBER 31, 2025
            2025  2024
            ------------ (US Dollars in '000) ---------
            Mark-up / return / interest earned  4,229,789  3,872,017
            Total income  1,514,536  918,319
            Profit after taxation  464,154  267,164
            """);

        Assert.Empty(report.Statements);
    }

    [Fact]
    public void A_bank_s_revenue_is_its_total_income()
    {
        var report = Read("""
            Unconsolidated Profit and Loss Account
            For the year ended December 31, 2025
            Note  2025  2024
            (Rupees in '000)
            Mark-up / return / interest earned  25  900,000,000  700,000,000
            Mark-up / return / interest expensed  26  (600,000,000)  (500,000,000)
            Net mark-up / interest income  300,000,000  200,000,000
            Total non-mark-up / interest income  119,560,197  60,908,948
            Total income  419,560,197  260,908,948
            Profit after taxation  128,008,847  80,527,534
            """);

        var s = Assert.Single(report.Statements);
        Assert.Equal("Total income", s.RevenueLine);
        Assert.Equal(419_560_197_000m, s.Revenue);
    }

    [Fact]
    public void Reads_the_chief_executive_s_pay_when_the_items_add_up()
    {
        // Chairman first, then the chief executive: each group split by year.
        var report = Read("""
            42  Remuneration of Chief Executive Officer, Directors and Executives
            The aggregate amounts charged in these financial statements during the year for remuneration, including certain
            benefits, to the chief executive officer, executive directors, non-executive directors and executives of the Company are
            as follows:
            Chairman  Chief Executive Officer  Executive Directors  Executives
            (Rupees in 000)  2025  2024  2025  2024  2025  2024  2025  2024
            Managerial remuneration / fee  10,050  9,528  137,084  127,251  57,974  71,889  4,153,641  3,736,163
            Bonus  -  -  17,885  16,372  20,291  14,099  820,079  716,709
            Retirement benefits  -  -  -  -  11,621  8,682  686,268  623,911
            Housing  -  -  26,880  25,200  -  4,450  101,453  96,103
            Reimbursable expenses  1,075  1,673  133,115  130,386  32,676  56,545  2,924,778  2,822,991
            11,125  11,201  314,964  299,209  122,562  155,665  8,686,219  7,995,877
            Number of persons  1  1  1  1  2  2  983  921
            42.1  The chairman and chief executive of the Company are provided with use of Company maintained vehicles.
            """);

        var pay = report.CeoPay!;
        Assert.Equal(2025, pay.FiscalYear);
        Assert.Equal(314_964_000m, pay.Total);
        Assert.Equal(137_084_000m, pay.Salary);
        Assert.Equal(17_885_000m, pay.Bonus);
    }

    [Fact]
    public void Reads_years_split_by_group_and_labelled_totals()
    {
        var report = Read("""
            45.  REMUNERATION OF CHIEF EXECUTIVE AND EXECUTIVES
            The aggregate amounts charged in the financial statements for remunerations, including all benefits to Chief
            Executive and Executives of the Company were as follows:
            2025
            Chief Executive  Executives  Total  Non-Executive Directors
            (Rupees)
            Managerial remuneration  11,062,400  12,425,298  23,487,698  -
            House rent  4,963,908  1,225,880  6,189,788  -
            Bonus  1,907,087  2,136,498  4,043,585  -
            Directors fee  -  -  -  850,000
            Total  17,933,395  15,787,676  33,721,071  850,000
            Number of persons  1  6  7  3
            2024
            Managerial remuneration  9,000,000  10,000,000  19,000,000  -
            Total  9,000,000  10,000,000  19,000,000  -
            """);

        Assert.Equal(17_933_395m, report.CeoPay!.Total);
    }

    [Fact]
    public void Uses_the_grand_total_below_a_subtotal()
    {
        // Maple Leaf Cement 2026: short-term benefits add up to a subtotal, then the provident fund and the grand total.
        var report = Read("""
            48. REMUNERATION OF CHIEF EXECUTIVE, DIRECTORS AND EXECUTIVES
            The aggregate amounts charged in the unconsolidated financial statements for the year for
            remuneration, including all benefits to the Chief Executive, Directors and Executives of the Company
            are as follows:
            2026
            Chief Executive  Executive Directors  Non-Executive Directors  Executives
            (----------------------- Rupees in thousand ----------------------- )
            Managerial remuneration  108,159  64,050  -  887,925
            Bonus  867,985  -  -  -
            House rent  7,215  2,463  -  173,510
            Medical  7,215  4,927  -  73,183
            Conveyance  5,059  2,388  -  152,531
            Utilities  6,383  4,358  -  98,375
            Advisory arrangement  -  -  1,363  -
            1,002,016  78,186  1,363  1,385,524
            Contribution to Provident Fund Trust  7,215  4,927  -  73,590
            1,009,231  83,113  1,363  1,459,114
            Number of persons  1  1  7  301
            """);

        Assert.Equal(1_009_231_000m, report.CeoPay!.Total);
        Assert.Equal(867_985_000m, report.CeoPay.Bonus);
    }

    [Fact]
    public void Leaves_out_pay_when_the_post_changed_hands()
    {
        var report = Read("""
            35.  REMUNERATION OF DIRECTORS, CHIEF EXECUTIVE AND EXECUTIVES
            The aggregate amounts of remuneration including benefits to Non-executive Directors, Chief Executive and
            Executives of the Company are as follows:
            2026  2025
            Non-Executive Directors  Chief Executive  Executives  Non-Executive Directors  Chief Executive  Executives
            ---------------------- (Rupees in thousand) ----------------------
            Fees  49,200  -  -  42,000  -  -
            Managerial remuneration  -  70,428  539,649  -  73,613  438,441
            49,200  70,428  539,649  42,000  73,613  438,441
            Number of persons  10  3*  126  10  1  127
            """);

        Assert.Null(report.CeoPay);
        Assert.Contains(report.Warnings, w => w.Contains("3 people"));
    }

    [Fact]
    public void Reads_a_managing_directors_pay_with_a_wrapped_row_by_column_position()
    {
        // PSO: the unit above the heading, and "Performance bonus" printed on two lines (2024's half above 2025's).
        PdfTextLine L(string text, params double[] x) => new(text, x);
        var lines = new List<PdfTextLine>
        {
            L("(Amounts in Rs. '000)", 514),
            L("35.2  Remuneration of Managing Director, Directors and Executives", 62, 220),
            L("35.2.1  The aggregate charge for the year in respect of remuneration and benefits to the Managing Director and Executives", 66, 324),
            L("are as follows:", 118),
            L("2025  2024", 368, 495),
            L("Managing  Executives  Managing  Executives", 334.8, 400.6, 463.6, 528.3),
            L("Director", 463.6),
            L("Managerial remuneration  30,612  2,060,334  25,559  1,819,644", 140, 333.7, 400.2, 465.4, 534.3),
            L("Housing and utilities  16,836  1,163,688  14,058  1,027,123", 130, 333.7, 400.2, 465.4, 534.3),
            L("6,863  441,147", 467.8, 538.1),
            L("Performance bonus  8,218  503,245", 128.7, 336.1, 403.8),
            L("Retirement benefits  6,167  926,822  4,780  1,067,955", 128.7, 336.1, 403.8, 467.8, 534.3),
            L("Other allowances and benefits  19,171  2,734,168  16,140  1,636,343", 149.5, 334.7, 400.2, 465.4, 534.3),
            L("81,004  7,416,057  67,400  6,015,958", 333.5, 400.2, 465.4, 534.3),
            L("worked part of the year  1  775  1  698", 144, 345.8, 412.6, 476, 546.3),
        };

        var pay = AnnualReportReader.Read(lines).CeoPay!;
        Assert.Equal(81_004_000m, pay.Total);
        Assert.Equal(8_218_000m, pay.Bonus);
    }

    [Fact]
    public void Finds_the_head_office_address()
    {
        var report = Read("""
            Pezu Plant (Registered Office)  Main Indus Highway, Pezu, Distt. Lakki Marwat, Khyber Pakhtunkhawa.
            Corporate Office and Mailing
            6-A, Muhammad Ali Housing Society, A. Aziz Hashim Tabba Street, Karachi-75350.
            Address
            """);

        Assert.StartsWith("6-A, Muhammad Ali Housing Society", report.HeadOffice);
    }

    [Theory]
    [InlineData("Profit after taxation  3,461,306,13 1  3,059,341,877", 3_461_306_131, 3_059_341_877)]   // glyph spacing inside a number
    [InlineData("Loss for the year  (599,668)  (186,362)", -599_668, -186_362)]
    [InlineData("Final tax  34  –  (24,785)", 0, -24_785)]
    public void Reads_amounts_in_a_row(string line, decimal current, decimal prior)
    {
        var row = AnnualReportReader.Row(line)!;
        Assert.Equal([current, prior], row.Values);
    }

    [Fact]
    public void Reads_the_exchange_profile()
    {
        var p = PsxProfile.Parse("""
            <div class="profile__item profile__item--decription"><div class="item__head">BUSINESS DESCRIPTION</div><p>Lucky Cement Limited was incorporated in Pakistan. </p></div>
            <div class="item__head">KEY PEOPLE</div><table class="tbl"><tbody class="tbl__body"><tr><td><strong>Muhammad Ali Tabba</strong></td><td>CEO</td></tr><tr><td><strong>Muhammad Sohail Tabba</strong></td><td>Chairperson</td></tr></tbody></table>
            <div class="item__head">ADDRESS</div><p>Main Indus Highway, Pezu, District Lakki Marwat, Khyber Pakhtunkhwa, Pakistan</p><div class="item__head">WEBSITE</div><p> <a href="http://www.lucky-cement.com" target="_blank">www.lucky-cement.com</a></p>
            <div class="item__head">Fiscal Year End</div><p>June</p>
            <div class="stats_item"><div class="stats_label">Market Cap (000'<span style="text-transform: lowercase;">s</span>)</div><div class="stats_value">81,936,985.05</div></div><div class="stats_item"><div class="stats_label">Shares</div><div class="stats_value">117,054,508</div></div>
            """);

        Assert.Equal(81_936_985.05m, p.MarketCapThousands);
        Assert.Equal(117_054_508, p.Shares);

        Assert.Equal("Muhammad Ali Tabba", p.Ceo);
        Assert.Equal("Muhammad Sohail Tabba", p.Chair);
        Assert.StartsWith("Main Indus Highway", p.Address);
        Assert.Equal("http://www.lucky-cement.com", p.Website);
        Assert.Equal("06-30", p.FiscalYearEndMonthDay());
    }

    [Theory]
    [InlineData("CEMENT", "Materials")]
    [InlineData("COMMERCIAL BANKS", "Finance")]
    [InlineData("TEXTILE COMPOSITE", "Consumer & retail")]
    [InlineData("TECHNOLOGY & COMMUNICATION", "Software & IT")]
    [InlineData("OIL & GAS EXPLORATION COMPANIES", "Energy & utilities")]
    [InlineData("AUTOMOBILE PARTS & ACCESSORIES", "Industrials")]
    [InlineData("MISCELLANEOUS", "Other")]
    public void Maps_exchange_sectors(string psx, string sector) => Assert.Equal(sector, PkImportPipeline.SectorOf(psx));
}
