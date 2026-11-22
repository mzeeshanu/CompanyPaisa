using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Core.Tests;

public class SecIdentityTests
{
    private const string WithEmail = "CompanyPaisa someone@test.invalid";

    [Theory]
    [InlineData("https://data.sec.gov/submissions/CIK0000320193.json", WithEmail)]
    [InlineData("https://www.sec.gov/files/company_tickers_exchange.json", WithEmail)]
    [InlineData("https://sec.gov/x", WithEmail)]
    [InlineData("http://www.sec.gov/x", "CompanyPaisa")]
    [InlineData("https://notsec.gov/x", "CompanyPaisa")]
    [InlineData("https://sec.gov.evil.test/x", "CompanyPaisa")]
    [InlineData("https://download.geonames.org/export/zip/US.zip", "CompanyPaisa")]
    public void The_contact_email_goes_to_the_sec_only(string url, string expected) =>
        Assert.Equal(expected, SecClient.IdentityFor(new Uri(url), WithEmail));

    [Fact]
    public void An_identity_without_an_email_is_sent_as_configured() =>
        Assert.Equal("CompanyPaisa/1.0 (+https://companypaisa.com)",
            SecClient.IdentityFor(new Uri("https://query.wikidata.org/sparql"), "CompanyPaisa/1.0 (+https://companypaisa.com)"));
}
