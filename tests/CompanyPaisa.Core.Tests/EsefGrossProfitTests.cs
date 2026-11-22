using System.Text.Json;
using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

public class EsefGrossProfitTests
{
    private static decimal Revenue(params (string Concept, decimal Value)[] lines)
    {
        var facts = lines.Append(("ProfitLoss", 72_700_000m)).Select((line, i) => (Key: $"f{i}", Fact: new
        {
            value = line.Item2,
            dimensions = new { concept = $"ifrs-full:{line.Item1}", period = "2025-01-01T00:00:00/2026-01-01T00:00:00", unit = "iso4217:EUR" }
        })).ToDictionary(x => x.Key, x => x.Fact);
        return EsefFinancials.Extract(JsonSerializer.Serialize(new { facts }), new DateOnly(2025, 12, 31)).Single().Revenue;
    }

    [Fact]
    public void A_top_line_below_gross_profit_becomes_cost_of_sales_plus_gross_profit() =>   // Medicover 2025
        Assert.Equal(2_378_100_000m, Revenue(("RevenueFromInterest", 4_300_000m), ("CostOfSales", 1_827_400_000m), ("GrossProfit", 550_700_000m)));

    [Fact]
    public void A_tagged_revenue_is_left_alone() =>
        Assert.Equal(4_300_000m, Revenue(("Revenue", 4_300_000m), ("CostOfSales", 1_827_400_000m), ("GrossProfit", 550_700_000m)));

    [Fact]
    public void Without_cost_of_sales_nothing_is_rebuilt() =>
        Assert.Equal(4_300_000m, Revenue(("RevenueFromInterest", 4_300_000m), ("GrossProfit", 550_700_000m)));
}
