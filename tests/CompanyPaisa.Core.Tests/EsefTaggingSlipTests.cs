using System.Text.Json;
using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

/// <summary>Years an ESEF report states in ways the extractor used to miss.</summary>
public class EsefTaggingSlipTests
{
    private static List<EsefYear> Extract(DateOnly reportEnd, params (string Concept, string Period, decimal Value)[] facts) =>
        EsefFinancials.Extract(JsonSerializer.Serialize(new
        {
            facts = facts.Select((f, i) => (Key: $"f{i}", Fact: new
            {
                value = f.Value,
                dimensions = new { concept = $"ifrs-full:{f.Concept}", period = f.Period, unit = "iso4217:EUR" }
            })).ToDictionary(x => x.Key, x => x.Fact)
        }), reportEnd);

    [Fact]
    public void A_years_revenue_tagged_as_one_day_is_the_year()   // Pirelli 2023
    {
        var years = Extract(new DateOnly(2023, 12, 31),
            ("Revenue", "2023-12-31T00:00:00/2024-01-01T00:00:00", 6_650_063_000m),
            ("ProfitLoss", "2023-01-01T00:00:00/2024-01-01T00:00:00", 495_905_000m),
            ("Revenue", "2022-12-31T00:00:00/2023-01-01T00:00:00", 6_615_727_000m),
            ("ProfitLoss", "2022-01-01T00:00:00/2023-01-01T00:00:00", 435_900_000m));
        Assert.Equal(6_650_063_000m, years.Single(y => y.FiscalYear == 2023).Revenue);
        Assert.Equal(6_615_727_000m, years.Single(y => y.FiscalYear == 2022).Revenue);
    }

    [Fact]
    public void A_company_with_no_sales_yet_has_revenue_of_zero()   // Cyad 2022
    {
        var years = Extract(new DateOnly(2022, 12, 31),
            ("Revenue", "2022-01-01T00:00:00/2023-01-01T00:00:00", 0m),
            ("ProfitLoss", "2022-01-01T00:00:00/2023-01-01T00:00:00", -40_935_000m));
        Assert.Equal(0m, years.Single().Revenue);
        Assert.Equal(-40_935_000m, years.Single().NetIncome);
    }

    [Fact]
    public void A_positive_revenue_line_wins_over_a_zero_one()
    {
        var years = Extract(new DateOnly(2022, 12, 31),
            ("Revenue", "2022-01-01T00:00:00/2023-01-01T00:00:00", 0m),
            ("RevenueFromContractsWithCustomers", "2022-01-01T00:00:00/2023-01-01T00:00:00", 5_000_000m),
            ("ProfitLoss", "2022-01-01T00:00:00/2023-01-01T00:00:00", -1_000_000m));
        Assert.Equal(5_000_000m, years.Single().Revenue);
    }

    [Fact]
    public void Revenue_tagged_only_by_kind_is_the_sum_of_the_kinds()   // Altarea 2024
    {
        var years = Extract(new DateOnly(2024, 12, 31),
            ("RevenueFromConstructionContracts", "2024-01-01T00:00:00/2025-01-01T00:00:00", 2_466_300_000m),
            ("RevenueFromRenderingOfServices", "2024-01-01T00:00:00/2025-01-01T00:00:00", 58_700_000m),
            ("RentalIncomeFromInvestmentProperty", "2024-01-01T00:00:00/2025-01-01T00:00:00", 243_500_000m),
            ("ProfitLossAttributableToOwnersOfParent", "2024-01-01T00:00:00/2025-01-01T00:00:00", 6_100_000m));
        Assert.Equal(2_768_500_000m, years.Single().Revenue);
    }

    [Fact]
    public void A_tagged_total_wins_over_its_parts()
    {
        var years = Extract(new DateOnly(2024, 12, 31),
            ("Revenue", "2024-01-01T00:00:00/2025-01-01T00:00:00", 1_000m),
            ("RevenueFromSaleOfGoods", "2024-01-01T00:00:00/2025-01-01T00:00:00", 700m),
            ("RevenueFromRenderingOfServices", "2024-01-01T00:00:00/2025-01-01T00:00:00", 300m),
            ("ProfitLoss", "2024-01-01T00:00:00/2025-01-01T00:00:00", 10m));
        Assert.Equal(1_000m, years.Single().Revenue);
    }

    [Theory]
    [InlineData("/2138008L93GYU5GN6179/2022-09-30/ESEF/GB/0/213800G4OJM9F7G9LG39-2022-09-30-T02.json", "213800G4OJM9F7G9LG39")]   // Hemogenyx
    [InlineData("/2138008L93GYU5GN6179/2024-12-31/ESEF/GB/0/2138008L93GYU5GN6179-2024-12-31-en.json", null)]
    [InlineData("/549300VEBQPHRZBKUX38/2023-12-31/ESEF/SE/0/investor-2023-12-31-sv.json", null)]
    public void A_report_named_for_another_companys_LEI_is_recognised(string url, string? other) =>
        Assert.Equal(other, EsefFinancials.ForeignLei(url, url.Split('/')[1]));

    [Fact]
    public void A_comparative_revenue_with_a_mistyped_end_but_the_years_start_counts()   // Whitbread 2025
    {
        var years = Extract(new DateOnly(2025, 2, 27),
            ("Revenue", "2024-03-01T00:00:00/2025-02-28T00:00:00", 2_921_900_000m),
            ("ProfitLoss", "2024-03-01T00:00:00/2025-02-28T00:00:00", 253_700_000m),
            ("Revenue", "2023-03-03T00:00:00/2024-03-30T00:00:00", 2_959_900_000m),
            ("ProfitLoss", "2023-03-03T00:00:00/2024-03-01T00:00:00", 312_100_000m));
        Assert.Equal(2_959_900_000m, years.Single(y => y.FiscalYear == 2024).Revenue);
    }
}
