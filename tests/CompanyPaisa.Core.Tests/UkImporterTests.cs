using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

public class UkImporterTests
{
    /// <summary>
    /// Shaped like Tesco's 2025/26 report: a PDF converted to positioned text, directors as column groups,
    /// a label ("PSP") on a different line from its numbers, and £'000 units.
    /// </summary>
    private const string ConvertedPdf = """
        <div class="t">Single total figure of remuneration – E<span class="_ _2"></span>xecutive Directors (audited)</div>
        <div class="t">The following table sets out the STFR for 2025/26 and 2024/25 for the Executive Directors.</div>
        <div class="t">Ken M<span class="_ _2"></span>urphy<span class="_ _18"> </span><span>Imran Nawaz</span></div>
        <div class="t">2025/26</div><div class="t">(£’000)</div><div class="t">2024/25</div><div class="t">(£’000)</div>
        <div class="t">2025/26</div><div class="t">(£’000)</div><div class="t">2024/25</div><div class="t">(£’000)</div>
        <div class="t">Fixed pay</div>
        <div class="t">Salary<span class="_ _3"> </span>1,515<span class="_ _3"> </span>1,454<span class="_ _3"> </span>840<span class="_ _3"> </span>790</div>
        <div class="t">Benefits 124 88 112 92</div>
        <div class="t">Pension 114 109 63 59</div>
        <div class="t">Total fixed pay 1,753 1,651 1,015 941</div>
        <div class="t">Annual bonus (cash and deferred shares) 3,424 2,885 1,949 1,616</div>
        <div class="t">PSP</div>
        <div class="t">(b)</div>
        <div class="t">5,665 5,227 2,750 2,513</div>
        <div class="t">Total variable pay 9,089 8,112 4,699 4,129</div>
        <div class="t">Total remuneration 10,842 9,763 5,714 5,070</div>
        <div class="t">Single total figure of remuneration – Non-executive Directors (audited)</div>
        """;

    [Fact]
    public void Reads_directors_as_column_groups_from_a_converted_pdf()
    {
        var result = RemunerationParser.Parse(ConvertedPdf);

        Assert.Equal("GBP", result.Currency);
        Assert.Equal(4, result.Rows.Count);
        var ken = Assert.Single(result.Rows, r => r.Name == "Ken Murphy" && r.Year == 2026);
        Assert.Equal(10_842_000m, ken.Total);
        Assert.Equal(1_515_000m, ken.Salary);
        Assert.Equal(3_424_000m, ken.Bonus);
        Assert.Equal(5_665_000m, ken.LongTerm);        // label and numbers were on separate lines
        Assert.Equal(238_000m, ken.Other);             // benefits + pension
        Assert.True(ken.Verified);
        var imran2025 = Assert.Single(result.Rows, r => r.Name == "Imran Nawaz" && r.Year == 2025);
        Assert.Equal(5_070_000m, imran2025.Total);
    }

    /// <summary>A real HTML table with one row per director and year (the other common layout).</summary>
    private const string RowsTable = """
        <h3>Single total figure of remuneration for Executive Directors (audited)</h3>
        <table>
          <tr><th></th><th>Year</th><th>Salary £000</th><th>Benefits £000</th><th>Pension £000</th><th>Annual bonus £000</th><th>LTIP £000</th><th>Total £000</th></tr>
          <tr><td>Jane Smith</td><td>2025</td><td>900</td><td>30</td><td>90</td><td>1,100</td><td>2,000</td><td>4,120</td></tr>
          <tr><td></td><td>2024</td><td>870</td><td>28</td><td>87</td><td>950</td><td>1,500</td><td>3,435</td></tr>
          <tr><td>Rob Jones</td><td>2025</td><td>600</td><td>20</td><td>60</td><td>700</td><td>–</td><td>1,380</td></tr>
        </table>
        <h3>Non-executive Directors' single figure</h3>
        """;

    [Fact]
    public void Reads_directors_as_rows_from_an_html_table()
    {
        var result = RemunerationParser.Parse(RowsTable);

        Assert.Equal(3, result.Rows.Count);
        var jane2024 = Assert.Single(result.Rows, r => r.Name == "Jane Smith" && r.Year == 2024);
        Assert.Equal(3_435_000m, jane2024.Total);
        Assert.Equal(870_000m, jane2024.Salary);
        Assert.Equal(1_500_000m, jane2024.LongTerm);
        Assert.True(jane2024.Verified);
        var rob = Assert.Single(result.Rows, r => r.Name == "Rob Jones");
        Assert.Equal(0m, rob.LongTerm);                // "–" means nothing vested
        Assert.Equal(1_380_000m, rob.Total);
    }

    [Fact]
    public void Reports_when_no_table_is_found()
    {
        var result = RemunerationParser.Parse("<p>Our strategy</p><p>Revenue grew 5%.</p>");
        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("no single total figure table"));
    }

    [Fact]
    public void Extracts_the_reported_year_and_its_comparative_from_esef_json()
    {
        const string json = """
            { "facts": {
              "f1": { "value": "73712000000", "dimensions": { "concept": "ifrs-full:Revenue", "entity": "lei:X", "period": "2025-02-23T00:00:00/2026-03-01T00:00:00", "unit": "iso4217:GBP" } },
              "f2": { "value": "69916000000", "dimensions": { "concept": "ifrs-full:Revenue", "entity": "lei:X", "period": "2024-02-25T00:00:00/2025-02-23T00:00:00", "unit": "iso4217:GBP" } },
              "f3": { "value": "1787000000", "dimensions": { "concept": "ifrs-full:ProfitLossAttributableToOwnersOfParent", "entity": "lei:X", "period": "2025-02-23T00:00:00/2026-03-01T00:00:00", "unit": "iso4217:GBP" } },
              "f4": { "value": "1626000000", "dimensions": { "concept": "ifrs-full:ProfitLossAttributableToOwnersOfParent", "entity": "lei:X", "period": "2024-02-25T00:00:00/2025-02-23T00:00:00", "unit": "iso4217:GBP" } },
              "f5": { "value": "999", "dimensions": { "concept": "ifrs-full:Revenue", "entity": "lei:X", "period": "2025-02-23T00:00:00/2026-03-01T00:00:00", "unit": "iso4217:GBP", "ifrs-full:SegmentsAxis": "x:UkMember" } }
            } }
            """;

        var years = EsefFinancials.Extract(json, new DateOnly(2026, 2, 28));

        Assert.Equal(2, years.Count);
        var fy26 = Assert.Single(years, y => y.FiscalYear == 2026);
        Assert.Equal(73_712_000_000m, fy26.Revenue);   // the segment fact is ignored
        Assert.Equal(1_787_000_000m, fy26.NetIncome);
        Assert.Equal("GBP", fy26.Currency);
        Assert.Contains(years, y => y.FiscalYear == 2025 && y.Revenue == 69_916_000_000m);
    }

    /// <summary>Unilever since 2022: the index lists the report at its filing date (9 February), not its year end.</summary>
    [Fact]
    public void Finds_the_year_when_the_index_gives_the_filing_date()
    {
        const string json = """
            { "facts": {
              "f1": { "value": "60073000000", "dimensions": { "concept": "ifrs-full:Revenue", "entity": "lei:X", "period": "2022-01-01T00:00:00/2023-01-01T00:00:00", "unit": "iso4217:EUR" } },
              "f2": { "value": "52444000000", "dimensions": { "concept": "ifrs-full:Revenue", "entity": "lei:X", "period": "2021-01-01T00:00:00/2022-01-01T00:00:00", "unit": "iso4217:EUR" } },
              "f3": { "value": "7642000000", "dimensions": { "concept": "ifrs-full:ProfitLossAttributableToOwnersOfParent", "entity": "lei:X", "period": "2022-01-01T00:00:00/2023-01-01T00:00:00", "unit": "iso4217:EUR" } },
              "f4": { "value": "6049000000", "dimensions": { "concept": "ifrs-full:ProfitLossAttributableToOwnersOfParent", "entity": "lei:X", "period": "2021-01-01T00:00:00/2022-01-01T00:00:00", "unit": "iso4217:EUR" } }
            } }
            """;

        var years = EsefFinancials.Extract(json, new DateOnly(2023, 2, 9));

        Assert.Equal([2022, 2021], years.Select(y => y.FiscalYear));
        Assert.Equal(60_073_000_000m, years[0].Revenue);
    }

    [Theory]
    [InlineData("Ken Murphy", true)]
    [InlineData("Dame Emma Walmsley", true)]
    [InlineData("Ignacio Bustamante", true)]
    [InlineData("Target Threshold Target Maximum Weighting", false)]
    [InlineData("STRATEGIC REPORT CORPORATE GOVERNANCE", false)]
    [InlineData("Customer Satisfaction", false)]
    [InlineData("David Lockwood David Mellors", false)]
    [InlineData("Additional Information", false)]
    [InlineData("Estimated Deferred", false)]
    [InlineData("Advanced Corporation Tax", false)]
    public void Tells_names_from_table_headings(string text, bool isName) =>
        Assert.Equal(isName, RemunerationParser.LooksLikeName(text));

    [Fact]
    public void Merges_initials_into_the_full_name_at_the_same_company()
    {
        var map = UkImportPipeline.CanonicalNames(["David Seekings", "D. Seekings", "Michelle Brukwicki", "M. Brukwicki", "Ms Halai", "Nina Halai", "J. Smith", "Jane Smith", "John Smith"]);
        Assert.Equal("David Seekings", map["D. Seekings"]);
        Assert.Equal("Michelle Brukwicki", map["M. Brukwicki"]);
        Assert.Equal("Nina Halai", map["Ms Halai"]);
        Assert.Equal("J. Smith", map["J. Smith"]);   // two Smiths fit — left alone rather than guessed
        Assert.Equal("Jane Smith", map["Jane Smith"]);
    }

    [Fact]
    public void Spots_the_company_name_in_its_own_pay_table()
    {
        Assert.True(UkImportPipeline.IsCompanyName("BAE Systems", "BAE Systems"));
        Assert.True(UkImportPipeline.IsCompanyName("Balfour Beatty", "Balfour Beatty plc"));
        Assert.False(UkImportPipeline.IsCompanyName("Charles Woodburn", "BAE Systems"));
    }

    [Theory]
    [InlineData("Rolls-Royce Holdings", "ROLLS-ROYCE HOLDINGS PLC")]
    [InlineData("Tesco", "TESCO PLC")]
    [InlineData("BP", "BP P.L.C.")]
    [InlineData("3i", "3I GROUP PLC")]
    [InlineData("Marks & Spencer Group", "MARKS AND SPENCER GROUP PLC")]
    public void Matches_index_names_to_legal_names(string indexName, string legalName) =>
        Assert.Equal(UkImportPipeline.NormaliseName(legalName), UkImportPipeline.NormaliseName(indexName));
}
