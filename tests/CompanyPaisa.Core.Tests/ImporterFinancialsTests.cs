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

    /// <summary>Acadia Healthcare 2018: "Revenues" held a $1.9bn sub-line while contract revenue had the $3.0bn total.</summary>
    [Fact]
    public void Takes_the_largest_revenue_line_for_each_period()
    {
        var json = $$"""
            { "facts": { "us-gaap": {
                "Revenues": { "units": { "USD": [ {{Fact("CY2018", "2018-01-01", "2018-12-31", 1_900_000_000m)}} ] } },
                "RevenueFromContractWithCustomerExcludingAssessedTax": { "units": { "USD": [ {{Fact("CY2018", "2018-01-01", "2018-12-31", 3_010_000_000m)}} ] } },
                "NetIncomeLoss": { "units": { "USD": [ {{Fact("CY2018", "2018-01-01", "2018-12-31", 150_000_000m)}} ] } }
            } } }
            """;

        var result = new XbrlFinancialsExtractor().Extract("ACHC", 1520697, json, years: 10);

        Assert.Equal(3_010_000_000m, Assert.Single(result.Periods).Revenue);
    }

    /// <summary>A company that moved from US GAAP to IFRS keeps its old US GAAP facts; the newer standard must win.</summary>
    [Fact]
    public void Uses_the_accounting_standard_with_the_newest_figures()
    {
        var json = $$"""
            { "facts": {
                "us-gaap": {
                  "Revenues": { "units": { "USD": [ {{Fact("CY2013", "2013-01-01", "2013-12-31", 1_640_000_000m)}} ] } },
                  "NetIncomeLoss": { "units": { "USD": [ {{Fact("CY2013", "2013-01-01", "2013-12-31", -600_000_000m)}} ] } } },
                "ifrs-full": {
                  "Revenue": { "units": { "USD": [ {{Fact("CY2025", "2025-01-01", "2025-12-31", 11_200_000_000m)}} ] } },
                  "ProfitLoss": { "units": { "USD": [ {{Fact("CY2025", "2025-01-01", "2025-12-31", 3_100_000_000m)}} ] } } }
            } }
            """;

        var result = new XbrlFinancialsExtractor().Extract("AEM", 2809, json, years: 20);

        Assert.Equal(2025, Assert.Single(result.Periods).FiscalYear);
    }

    [Fact]
    public void Never_derives_a_negative_fourth_quarter()
    {
        // Three quarters measured on a bigger basis than the year: year minus quarters would be negative.
        var json = $$"""
            { "facts": { "us-gaap": {
                "Revenues": { "units": { "USD": [
                  {{Fact("CY2023", "2023-01-01", "2023-12-31", 100m)}}, {{Fact("CY2023Q1", "2023-01-01", "2023-03-31", 50m)}},
                  {{Fact("CY2023Q2", "2023-04-01", "2023-06-30", 50m)}}, {{Fact("CY2023Q3", "2023-07-01", "2023-09-30", 50m)}} ] } },
                "NetIncomeLoss": { "units": { "USD": [
                  {{Fact("CY2023", "2023-01-01", "2023-12-31", 10m)}}, {{Fact("CY2023Q1", "2023-01-01", "2023-03-31", 1m)}},
                  {{Fact("CY2023Q2", "2023-04-01", "2023-06-30", 1m)}}, {{Fact("CY2023Q3", "2023-07-01", "2023-09-30", 1m)}} ] } }
            } } }
            """;

        var result = new XbrlFinancialsExtractor().Extract("X", 1, json, years: 10);

        Assert.DoesNotContain(result.Periods, p => p.Revenue < 0);
        Assert.Equal(3, result.Periods.Count(p => p.PeriodType == PeriodType.Quarterly));
    }
}
