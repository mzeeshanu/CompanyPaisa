using System.Text.Json;
using CompanyPaisa.Importer.Financials;
using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Core.Tests;

public class SourceSlipTests
{
    private static object Fact(int year, decimal value, string form = "10-K", bool framed = true, string accession = "A-1") => new
    {
        start = $"{year}-01-01", end = $"{year}-12-31", val = value, form, filed = $"{year + 1}-03-01", accn = accession, frame = framed ? $"CY{year}" : null
    };

    private static FinancialsResult Extract(object gaap) =>
        new XbrlFinancialsExtractor().Extract("X", 1, JsonSerializer.Serialize(new Dictionary<string, object> { ["facts"] = new Dictionary<string, object> { ["us-gaap"] = gaap } }), 10);

    private static object Concept(params object[] facts) => new { units = new { USD = facts } };

    [Fact]
    public void A_revised_proxy_is_still_a_proxy()   // Xcel Brands
    {
        var result = Extract(new { Revenues = Concept(Fact(2025, 4_939_000m)),
            NetIncomeLoss = Concept(Fact(2025, -17_461_000m, framed: false), Fact(2025, -17_461_000_000_000m, "DEFR14A", accession: "P-1")) });
        Assert.Equal(-17_461_000m, result.Periods.Single().NetIncome);
    }

    [Fact]
    public void A_scale_slip_in_an_amended_report_yields_to_the_report()   // INVE
    {
        var result = Extract(new { Revenues = Concept(Fact(2021, 103_769_000m)),
            NetIncomeLoss = Concept(Fact(2021, 1_620_000m, framed: false), Fact(2021, 1_620_000_000_000m, "10-K/A", accession: "A-2")) });
        Assert.Equal(1_620_000m, result.Periods.Single().NetIncome);
    }

    [Fact]
    public void A_growth_company_ceiling_is_not_revenue()   // IonQ
    {
        var result = Extract(new { Revenues = Concept(Fact(2022, 1_235_000_000m)),
            RevenueFromContractWithCustomerExcludingAssessedTax = Concept(Fact(2022, 11_131_000m)),
            NetIncomeLoss = Concept(Fact(2022, -48_511_000m)) });
        Assert.Equal(11_131_000m, result.Periods.Single().Revenue);
    }

    [Fact]
    public void A_big_company_can_have_exactly_that_revenue()
    {
        var result = Extract(new { Revenues = Concept(Fact(2021, 990_000_000m), Fact(2022, 1_235_000_000m)),
            NetIncomeLoss = Concept(Fact(2021, 50_000_000m), Fact(2022, 60_000_000m)) });
        Assert.Contains(result.Periods, p => p.Revenue == 1_235_000_000m);
    }

    [Fact]
    public void Rent_is_a_landlords_revenue()   // Camden
    {
        var result = Extract(new { Revenues = Concept(Fact(2025, 12_967_000m)), OperatingLeaseLeaseIncome = Concept(Fact(2025, 1_573_544_000m)),
            NetIncomeLoss = Concept(Fact(2025, 384_462_000m)) });
        Assert.Equal(1_573_544_000m, result.Periods.Single().Revenue);
    }

    [Fact]
    public void Rent_in_the_wrong_units_is_not_revenue()   // MKZR
    {
        var result = Extract(new { Revenues = Concept(Fact(2025, 4_594_058m)), OperatingLeaseLeaseIncome = Concept(Fact(2025, 4_594_058_000_000m)),
            NetIncomeLoss = Concept(Fact(2025, -4_474_841m)) });
        Assert.Equal(4_594_058m, result.Periods.Single().Revenue);
    }

    [Fact]
    public void A_profit_contradicted_by_the_whole_and_pretax_result_is_a_loss()   // MBIA
    {
        var result = Extract(new { Revenues = Concept(Fact(2024, 42_000_000m)), NetIncomeLoss = Concept(Fact(2024, 435_000_000m)),
            ProfitLoss = Concept(Fact(2024, -444_000_000m)),
            IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest = Concept(Fact(2024, -441_000_000m)) });
        Assert.Equal(-444_000_000m, result.Periods.Single().NetIncome);
    }

    [Fact]
    public void A_fund_managers_fees_are_its_revenue()   // Amundi
    {
        var facts = new[] { ("RevenueFromInterest", 166_647_000m), ("ProfitLoss", 1_589_238_000m), ("FeeAndCommissionIncome", 6_400_280_000m), ("FeeAndCommissionExpense", 3_200_926_000m) }
            .ToDictionary(l => l.Item1, l => new { value = l.Item2, dimensions = new { concept = $"ifrs-full:{l.Item1}", period = "2025-01-01T00:00:00/2026-01-01T00:00:00", unit = "iso4217:EUR" } });
        Assert.Equal(3_199_354_000m, EsefFinancials.Extract(JsonSerializer.Serialize(new { facts }), new DateOnly(2025, 12, 31)).Single().Revenue);
    }

    [Theory]
    [InlineData("B&M", "MITCHELLS & BUTLERS PLC", false)]
    [InlineData("Bellway", "AJ BELL PLC", false)]
    [InlineData("GlobalData", "HANSARD GLOBAL PLC", false)]
    [InlineData("Sainsbury's", "J SAINSBURY PLC", true)]
    [InlineData("JD Sports", "JD SPORTS FASHION PLC", true)]
    [InlineData("Wetherspoons", "J D WETHERSPOON PLC", true)]
    public void A_name_matches_only_its_own_company(string listed, string legal, bool match) =>
        Assert.Equal(match, UkImportPipeline.FuzzyMatch(listed, [new UkEntity("LEI", legal, [])]) is not null);
}
