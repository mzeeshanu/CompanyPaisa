using CompanyPaisa.Contracts;
using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

public class XbrlFinancialsExtractorTests
{
    private static string Fact(string frame, string start, string end, decimal val) =>
        $$"""{ "start": "{{start}}", "end": "{{end}}", "val": {{val}}, "frame": "{{frame}}", "accn": "0001-24-000001" }""";

    /// <summary>Shaped like a Canadian 40-F filer's company facts: IFRS concepts, amounts in Canadian dollars, no quarters.</summary>
    [Fact]
    public void Reads_ifrs_figures_in_canadian_dollars()
    {
        var json = $$"""
            { "facts": {
                "dei": { "EntityCommonStockSharesOutstanding": { "units": { "shares": [] } } },
                "ifrs-full": {
                  "Revenue": { "units": { "CAD": [ {{Fact("CY2023", "2022-11-01", "2023-10-31", 56_129_000_000m)}}, {{Fact("CY2024", "2023-11-01", "2024-10-31", 57_344_000_000m)}} ] } },
                  "ProfitLoss": { "units": { "CAD": [ {{Fact("CY2023", "2022-11-01", "2023-10-31", 14_866_000_000m)}}, {{Fact("CY2024", "2023-11-01", "2024-10-31", 16_240_000_000m)}} ] } }
                } } }
            """;

        var result = new XbrlFinancialsExtractor().Extract("RY", 1000275, json, years: 10);

        Assert.Equal("CAD", result.Currency);
        Assert.Equal("Revenue", result.RevenueConcept);
        var annual = result.Periods.Where(p => p.PeriodType == PeriodType.Annual).OrderBy(p => p.FiscalYear).ToList();
        Assert.Equal([2023, 2024], annual.Select(p => p.FiscalYear));
        Assert.Equal(57_344_000_000m, annual[^1].Revenue);
        Assert.Equal(16_240_000_000m, annual[^1].NetIncome);
    }

    [Fact]
    public void Prefers_us_dollars_when_a_company_reports_in_them()
    {
        var json = $$"""
            { "facts": { "us-gaap": {
                "Revenues": { "units": { "USD": [ {{Fact("CY2024", "2024-01-01", "2024-12-31", 8_880_000_000m)}} ], "CAD": [ {{Fact("CY2024", "2024-01-01", "2024-12-31", 12_000_000_000m)}} ] } },
                "NetIncomeLoss": { "units": { "USD": [ {{Fact("CY2024", "2024-01-01", "2024-12-31", 2_020_000_000m)}} ] } }
            } } }
            """;

        var result = new XbrlFinancialsExtractor().Extract("SHOP", 1594805, json, years: 10);

        Assert.Equal("USD", result.Currency);
        Assert.Equal(8_880_000_000m, Assert.Single(result.Periods).Revenue);
    }
}
