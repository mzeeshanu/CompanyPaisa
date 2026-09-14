using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class CurrencyTests
{
    private static readonly CurrencyConverter Fx = new(Opt.Monitor(new CurrencyOptions
    {
        UsdPer = new(StringComparer.OrdinalIgnoreCase) { ["USD"] = 1m, ["GBP"] = 1.25m, ["EUR"] = 1.10m }
    }));

    [Fact]
    public void Converts_between_currencies_through_the_dollar()
    {
        Assert.Equal(125m, Fx.ToUsd(100m, "GBP"));
        Assert.Equal(80m, Fx.Convert(100m, "USD", "GBP"));
        Assert.Equal(100m, Fx.Convert(100m, "gbp", "GBP"));   // same currency: untouched
        Assert.Equal(100m, Fx.ToUsd(100m, "JPY"));            // no rate configured: taken as is
    }

    [Fact]
    public void A_mixed_total_is_expressed_in_the_currency_most_results_use() =>
        Assert.Equal("GBP", Fx.Dominant(["GBP", "USD", "GBP", "EUR", "GBP"]));
}
