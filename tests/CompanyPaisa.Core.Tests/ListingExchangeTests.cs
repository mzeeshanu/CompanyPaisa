using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Core.Tests;

public class ListingExchangeTests
{
    private const string Directory =
        "ACT Symbol|Security Name|Exchange|CQS Symbol|ETF|Round Lot Size|Test Issue|NASDAQ Symbol\r\n" +
        "JPM|JP Morgan Chase & Co. Common Stock|N|JPM|N|100|N|JPM\r\n" +
        "IMO|Imperial Oil Limited Common Stock|A|IMO|N|100|N|IMO\r\n" +
        "BRK.B|Berkshire Hathaway Inc. New Common Stock|N|BRK.B|N|40|N|BRK.B\r\n" +
        "SPY|SPDR S&P 500 ETF Trust|P|SPY|Y|100|N|SPY\r\n" +
        "CBOE|Cboe Global Markets, Inc. Common Stock|Z|CBOE|N|100|N|CBOE\r\n" +
        "ZTST|Test Issue|A|ZTST|N|100|Y|ZTST\r\n" +
        "File Creation Time: 0923202608:31||||||\r\n";

    [Theory]
    [InlineData("JPM", "NYSE")]
    [InlineData("IMO", "NYSE American")]
    [InlineData("BRK-B", "NYSE")]
    [InlineData("SPY", "NYSE Arca")]
    [InlineData("CBOE", "Cboe")]
    public void The_directory_gives_each_listing_its_exchange(string ticker, string exchange) =>
        Assert.Equal(exchange, ListingExchanges.Parse(Directory)[ticker]);

    [Fact]
    public void Test_issues_and_the_footer_are_skipped()
    {
        var map = ListingExchanges.Parse(Directory);
        Assert.False(map.ContainsKey("ZTST"));
        Assert.Equal(5, map.Count);
    }

    [Theory]
    [InlineData("IMO", "NYSE", "NYSE American")]
    [InlineData("JPM", "NYSE", "NYSE")]
    [InlineData("CBOE", "CBOE", "Cboe")]
    [InlineData("UNKNOWN", "NYSE", "NYSE")]
    [InlineData("IMO", "Nasdaq", "Nasdaq")]
    [InlineData("IMO", "OTC", "OTC")]
    public void Only_the_SECs_shared_names_are_made_exact(string ticker, string secExchange, string stored) =>
        Assert.Equal(stored, ListingExchanges.Refine(ticker, secExchange, ListingExchanges.Parse(Directory)));
}
