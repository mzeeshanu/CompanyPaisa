using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class PriceSymbolTests
{
    private static readonly PriceWidgetOptions Options = new()
    {
        Exchanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nasdaq"] = "NASDAQ", ["NYSE"] = "NYSE", ["NYSE American"] = "AMEX", ["Euronext Paris"] = "EURONEXT", ["Borsa Italiana"] = "MIL", ["Bolsa de Madrid"] = "BME",
        },
    };

    private static Company Listed(string ticker, string exchange) =>
        new() { CompanyId = ticker, Name = ticker, Ticker = ticker, Exchange = exchange, Sector = "Technology" };

    [Theory]
    [InlineData("AAPL", "Nasdaq", "NASDAQ:AAPL")]
    [InlineData("BRK-B", "NYSE", "NYSE:BRK.B")]
    [InlineData("IMO", "NYSE American", "AMEX:IMO")]
    [InlineData("MC.PA", "Euronext Paris", "EURONEXT:MC")]
    [InlineData("ENI.MI", "Borsa Italiana", "MIL:ENI")]
    [InlineData("SAN.MC", "bolsa de madrid", "BME:SAN")]
    public void Listed_companies_get_the_TradingView_symbol(string ticker, string exchange, string symbol) =>
        Assert.Equal(symbol, PriceSymbols.For(Listed(ticker, exchange), Options));

    [Theory]
    [InlineData("SHEL", "LSE")]
    [InlineData("OGDC.KA", "Pakistan Stock Exchange")]
    [InlineData("ABCD", "OTC")]
    [InlineData("SNUS-PH", "NYSE")]
    [InlineData("ABC-WS", "Nasdaq")]
    public void Exchanges_that_block_the_widgets_get_none(string ticker, string exchange) =>
        Assert.Null(PriceSymbols.For(Listed(ticker, exchange), Options));

    [Fact]
    public void Switched_off_gives_none()
    {
        var off = new PriceWidgetOptions { Enabled = false, Exchanges = Options.Exchanges };
        Assert.Null(PriceSymbols.For(Listed("AAPL", "Nasdaq"), off));
    }
}
