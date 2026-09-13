using System.Text;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Core.Tests;

public class InsiderTests
{
    // Shape copied from EDGAR's issuer page (/cgi-bin/own-disp?action=getissuer), "Ownership Reports from" table.
    private const string Html = """
        <table border="1" cellspacing="0" cellpadding="3">
        <tr> <td align="left"><a class="header" href="#">Owner</a></td> <td>Filings</td> <td>Transaction Date</td> <td>Type of Owner</td> </tr>
        <tr><td valign='top'><a href="/cgi-bin/own-disp?action=getowner&amp;CIK=0001181178">NARAYEN SHANTANU</a></td>
            <td><a href="/cgi-bin/browse-edgar?action=getcompany&amp;CIK=0001181178">0001181178</a></td> <td>2026-03-02</td> <td>director, officer: Chair &amp; CEO</td> </tr>
        <tr><td valign='top'><a href="/cgi-bin/own-disp?action=getowner&amp;CIK=0001643724">Pentland Adele Louise</a></td>
            <td><a href="#">0001643724</a></td> <td>2025-06-16</td> <td>officer: Chief Legal Officer & EVP</td> </tr>
        <tr><td valign='top'><a href="/cgi-bin/own-disp?action=getowner&amp;CIK=0001000001">Smith Robert J</a></td>
            <td><a href="#">0001000001</a></td> <td>2024-01-10</td> <td>officer: CFO</td> </tr>
        <tr><td valign='top'><a href="/cgi-bin/own-disp?action=getowner&amp;CIK=0001000002">Smith Karen</a></td>
            <td><a href="#">0001000002</a></td> <td>2023-05-01</td> <td>director</td> </tr>
        <tr><td valign='top'><a href="/cgi-bin/own-disp?action=getowner&amp;CIK=0001000003">McDermott-Spikes Tricia S</a></td>
            <td><a href="#">0001000003</a></td> <td>2025-02-01</td> <td>officer: EVP</td> </tr>
        </table>
        """;

    private static readonly IReadOnlyList<SecInsider> Insiders = InsiderParser.Parse(Html);

    [Fact]
    public void Parses_owner_rows_with_cik_date_and_role()
    {
        Assert.Equal(5, Insiders.Count);
        var ceo = Insiders[0];
        Assert.Equal(1181178, ceo.Cik);
        Assert.Equal("NARAYEN SHANTANU", ceo.Name);
        Assert.Equal(new DateOnly(2026, 3, 2), ceo.LastFiling);
        Assert.True(ceo.IsOfficer);
        Assert.False(Insiders[3].IsOfficer);
    }

    [Theory]
    [InlineData("Shantanu Narayen", 1181178)]            // SEC order is "Last First"
    [InlineData("Adele L. Pentland", 1643724)]           // middle initial in the proxy only
    [InlineData("Bob Smith", 1000001)]                   // nickname → Robert, not Karen Smith
    [InlineData("Robert J. Smith, Jr.", 1000001)]        // suffix ignored
    [InlineData("Tricia S. McDermott-Spikes", 1000003)]  // hyphenated surname
    public void Matches_proxy_names_to_insiders(string proxyName, long cik) =>
        Assert.Equal(cik, InsiderMatcher.Match(proxyName, Insiders)?.Cik);

    [Theory]
    [InlineData("John Doe")]       // not an insider at this company
    [InlineData("Samantha Smith")] // shares a surname, but neither first name nor initial fits
    public void Leaves_unknown_people_unmatched(string proxyName) =>
        Assert.Null(InsiderMatcher.Match(proxyName, Insiders));

    [Fact]
    public void Cache_compression_round_trips_and_reads_old_plain_files()
    {
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<td>Summary Compensation Table</td>", 200)));
        var packed = SecClient.Pack(text);
        Assert.True(packed.Length < text.Length / 5);
        Assert.Equal(text, SecClient.Unpack(packed));
        Assert.Equal(text, SecClient.Unpack(text));            // uncompressed file from an older run
        var zip = "PKrest"u8.ToArray();
        Assert.Same(zip, SecClient.Pack(zip));                  // .zip downloads are stored as-is
    }
}
