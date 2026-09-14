using CompanyPaisa.Importer.Compensation;

namespace CompanyPaisa.Core.Tests;

public class SummaryCompensationTableParserTests
{
    // Shape copied from a real DEF 14A: names split across rows, footnote cells, "$" cells, dashes for zero.
    private const string Html = """
        <p>Summary Compensation Table</p>
        <table>
          <tr><td>Name and Principal Position</td><td>Year</td><td>Salary ($)</td><td>Bonus ($)</td><td>Stock Awards ($) (1)</td>
              <td>Non-Equity Plan Compensation (2)</td><td>All Other Compensation ($)</td><td>Total ($)</td></tr>
          <tr><td>Steven R. Fife , President and</td><td>2025</td><td>$</td><td>550,000</td><td>&#8212;</td><td>1,605,326</td><td>590,590</td><td>70,212</td><td>(3)</td><td>2,816,128</td></tr>
          <tr><td>Chief Executive Officer</td><td>2024</td><td>537,500</td><td>&#8212;</td><td>1,478,697</td><td>164,049</td><td>117,646</td><td>2,297,892</td></tr>
          <tr><td>Julie Boyster , Chief</td><td>2025</td><td>342,500</td><td>-</td><td>412,032</td><td>214,760</td><td>32,408</td><td>(4)</td><td>1,001,700</td></tr>
          <tr><td>Marketing Officer</td><td>2024</td><td>315,000</td><td>-</td><td>308,637</td><td>54,541</td><td>31,489</td><td>709,667</td></tr>
          <tr><td>Todd Thompson , Chief Information and Innovation</td><td>2025</td><td>184,167</td><td>-</td><td>694,875</td><td>121,697</td><td>17,698</td><td>(6)</td><td>1,018,437</td></tr>
          <tr><td>Officer (5)</td></tr>
        </table>
        """;

    [Fact]
    public void Reads_people_titles_years_and_components()
    {
        var result = new SummaryCompensationTableParser().Parse(Html);

        Assert.Equal(5, result.Rows.Count);
        var fife25 = result.Rows.Single(r => r.Name == "Steven R. Fife" && r.Year == 2025);
        Assert.Equal("President and Chief Executive Officer", fife25.Title);
        Assert.Equal(550_000, fife25.Salary);
        Assert.Equal(1_605_326, fife25.StockAwards);
        Assert.Equal(590_590 + 70_212, fife25.Other);
        Assert.Equal(2_816_128, fife25.Total);
        Assert.True(fife25.ComponentsVerified);

        Assert.Equal("Chief Marketing Officer", result.Rows.Single(r => r.Name == "Julie Boyster" && r.Year == 2024).Title);
        Assert.Equal("Chief Information and Innovation Officer", result.Rows.Single(r => r.Name == "Todd Thompson").Title);
    }

    // Common layout variant: two-row header, colspan'd "$"+value cells, name on its own line above the title.
    private const string SpannedHtml = """
        <table>
          <tr><td rowspan="2">Name and Principal Position</td><td rowspan="2">Year</td><td colspan="2">Salary</td><td colspan="2">Bonus</td>
              <td colspan="2">Stock Awards</td><td colspan="2">Option Awards</td><td colspan="2">All Other Compensation</td><td colspan="2">Total</td></tr>
          <tr><td colspan="2">($)</td><td colspan="2">($)</td><td colspan="2">($)</td><td colspan="2">($)</td><td colspan="2">($)</td><td colspan="2">($)</td></tr>
          <tr><td>Jamie Iannone</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>
          <tr><td>President and Chief Executive Officer (2)</td><td>2025</td><td>$</td><td>1,250,000</td><td></td><td>—</td><td>$</td><td>15,000,000</td><td></td><td>—</td><td>$</td><td>250,000</td><td>$</td><td>16,500,000</td></tr>
          <tr><td></td><td>2024</td><td>$</td><td>1,200,000</td><td></td><td>—</td><td>$</td><td>14,000,000</td><td></td><td>—</td><td>$</td><td>300,000</td><td>$</td><td>15,500,000</td></tr>
          <tr><td>Steve Priest (3)</td><td></td></tr>
          <tr><td>Chief Financial Officer</td><td>2025</td><td>$</td><td>800,000</td><td>$</td><td>50,000</td><td>$</td><td>5,000,000</td><td></td><td>—</td><td>$</td><td>150,000</td><td>$</td><td>6,000,000</td></tr>
        </table>
        """;

    [Fact]
    public void Handles_multi_row_headers_colspans_and_names_on_their_own_line()
    {
        var rows = new SummaryCompensationTableParser().Parse(SpannedHtml).Rows;

        Assert.Equal(3, rows.Count);
        var ceo = rows.Single(r => r.Name == "Jamie Iannone" && r.Year == 2025);
        Assert.Equal("President and Chief Executive Officer", ceo.Title);
        Assert.Equal((1_250_000m, 0m, 15_000_000m, 250_000m, 16_500_000m), (ceo.Salary, ceo.Bonus, ceo.StockAwards, ceo.Other, ceo.Total));
        Assert.True(ceo.ComponentsVerified);
        Assert.Equal(15_500_000, rows.Single(r => r.Name == "Jamie Iannone" && r.Year == 2024).Total);
        var cfo = rows.Single(r => r.Name == "Steve Priest");
        Assert.Equal("Chief Financial Officer", cfo.Title);
        Assert.Equal(50_000, cfo.Bonus);
    }

    [Theory]
    [InlineData("Brad Bentley (5) Executive Vice President", "Brad Bentley", "Executive Vice President")]
    [InlineData("Todd King 5", "Todd King", "")]
    [InlineData("Todd King 5 Chief Legal and Compliance Officer", "Todd King", "Chief Legal and Compliance Officer")]
    [InlineData("Jonathan E. Johnson III (7)", "Jonathan E. Johnson III", "")]
    [InlineData("Robert L. Smith, Jr., Chief Financial Officer", "Robert L. Smith, Jr.", "Chief Financial Officer")]
    [InlineData("David Wright (3) Co-Founder; Chairman of the Board", "David Wright", "Co-Founder; Chairman of the Board")]
    public void Splits_names_from_titles_and_footnotes(string text, string name, string title) =>
        Assert.Equal((name, title), SummaryCompensationTableParser.SplitNameAndTitle(text));

    /// <summary>Shaped like PROG Holdings' 2026 proxy: a bare footnote "6" in its own cell before the Total.</summary>
    private const string BareFootnoteHtml = """
        <table>
          <tr><td>Name and Principal Position</td><td>Year</td><td>Salary ($)</td><td>Bonus ($)</td><td>Stock Awards ($)</td><td>Option Awards ($)</td><td>Non-Equity Incentive Plan Compensation ($)</td><td>All Other Compensation ($)</td><td>Total ($)</td></tr>
          <tr><td>Steven A. Michaels Chief Executive Officer</td><td>2025</td><td>1,000,000</td><td>—</td><td>7,424,432</td><td>—</td><td>1,408,500</td><td>47,498</td><td>6</td><td>9,880,430</td></tr>
          <tr><td></td><td>2024</td><td>1,000,000</td><td>—</td><td>10,044,764</td><td>—</td><td>2,256,000</td><td>27,600</td><td>13,328,364</td></tr>
          <tr><td>Wahid Nawabi President</td><td>2026</td><td>1,118,829 5</td><td>—</td><td>13,339,715</td><td>—</td><td>767,609</td><td>27,623</td><td>15,253,775</td></tr>
        </table>
        """;

    [Fact]
    public void Ignores_a_bare_footnote_number_between_amounts()
    {
        var rows = new SummaryCompensationTableParser().Parse(BareFootnoteHtml).Rows;
        var y2025 = rows.Single(r => r.Name == "Steven A. Michaels" && r.Year == 2025);
        Assert.Equal(9_880_430m, y2025.Total);
        Assert.Equal(7_424_432m, y2025.StockAwards);
        Assert.True(y2025.ComponentsVerified);

        var spaced = rows.Single(r => r.Name == "Wahid Nawabi");   // "1,118,829 5": footnote after a space
        Assert.Equal(1_118_829m, spaced.Salary);
        Assert.True(spaced.ComponentsVerified);
    }

    [Fact]
    public void Returns_nothing_when_there_is_no_compensation_table() =>
        Assert.Empty(new SummaryCompensationTableParser().Parse("<table><tr><td>Director</td><td>Fees</td></tr></table>").Rows);
}
