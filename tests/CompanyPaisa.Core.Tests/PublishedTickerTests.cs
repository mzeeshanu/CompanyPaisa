using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Core.Tests;

public class PublishedTickerTests
{
    private static SecCompany Company(string[] tickers, string[] exchanges) =>
        new(1, "X", tickers, exchanges, null, "", null, null, null, []);

    private static readonly HashSet<string> Published = new(StringComparer.OrdinalIgnoreCase) { "SPG", "TCBI" };

    [Fact]
    public void A_company_keeps_its_published_ticker_while_the_sec_lists_it()
    {
        var kept = EdgarService.KeepPublishedTicker(Company(["SPGJ", "SPG"], ["", "NYSE"]), Published, [("SPG", "NYSE"), ("SPG-PJ", "NYSE")]);

        Assert.Equal(("SPG", "NYSE"), (kept.PrimaryTicker, kept.PrimaryExchange));
    }

    [Fact]
    public void A_ticker_the_sec_no_longer_lists_gives_way_to_the_new_one()
    {
        var renamed = EdgarService.KeepPublishedTicker(Company(["TXCP", "TCBI"], ["TXSE", "TXSE"]), Published, [("TXCP", "TXSE")]);

        Assert.Equal("TXCP", renamed.PrimaryTicker);
    }
}
