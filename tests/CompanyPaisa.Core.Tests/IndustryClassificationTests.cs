using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer;
using CompanyPaisa.Importer.Enrichment;
using CompanyPaisa.Importer.Validation;

namespace CompanyPaisa.Core.Tests;

/// <summary>Categories as the exchanges publish them, the broad sectors they map to, and the audit log.</summary>
public class IndustryClassificationTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Fact]
    public void Euronext_factsheet_gives_all_four_ICB_levels()
    {
        const string html = """
            <div><h3>ICB SECTORIAL CLASSIFICATION</h3>
            <div><span>Industry</span></div><div><span>35, Real Estate</span></div>
            <div><span>SuperSector</span></div><div><span>3510, Real Estate</span></div>
            <div><span>Sector</span></div><div><span>351020, Real Estate Investment Trusts</span></div>
            <div><span>Subsector</span></div><div><span>35102030, Office REITs</span></div></div>
            """;
        var levels = CategoryFinder.ReadEuronext(html)!;
        Assert.Equal(("35", "Real Estate"), levels.Industry);
        Assert.Equal(("35102030", "Office REITs"), levels.Subsector);
        Assert.Null(CategoryFinder.ReadEuronext("<div>No data</div>"));
    }

    [Fact]
    public void Nasdaq_summary_gives_the_ICB_code_and_super_sector()
    {
        const string json = """{"data":{"summaryData":{"icbCode":{"label":"ICB Code","value":"4510"},"sectorName":{"label":"Sector","value":"Food, Beverage and Tobacco"}}}}""";
        Assert.Equal(("4510", "Food, Beverage and Tobacco"), CategoryFinder.NordicSummary(json));
    }

    [Fact]
    public void Exchange_spellings_become_the_schemes_own_names_and_codes()   // LSE: "Financial Services ", "Travel & Leisure"
    {
        var k = CategoryFinder.Icb("London Stock Exchange", "Financials", "Financial Services ", null, null, Today);
        Assert.Equal(("ICB", "3020", "Financials", "Financial Services"), (k.Scheme, k.Code, k.Industry, k.Name));
        Assert.Equal("4050", CategoryFinder.Icb("London Stock Exchange", "Consumer Discretionary", "Travel & Leisure", null, null, Today).Code);
    }

    [Theory]
    [InlineData("4510", "Consumer & retail")]
    [InlineData("3010", "Finance")]
    [InlineData("10102010", "Semiconductors")]
    [InlineData("10102030", "Technology hardware")]
    [InlineData("10101015", "Software & IT")]
    [InlineData("40501010", "Transportation")]      // airlines
    [InlineData("4030", "Media & telecom")]
    [InlineData("50205020", "Business services")]
    [InlineData("6510", "Energy & utilities")]
    public void ICB_codes_map_to_the_sites_sectors(string code, string broad) => Assert.Equal(broad, Icb.Broad(code));

    [Theory]
    [InlineData(7363, "Business services")]   // help supply (staffing)
    [InlineData(7311, "Business services")]   // advertising agencies
    [InlineData(2869, "Materials")]           // industrial organic chemicals
    [InlineData(2621, "Materials")]           // paper mills
    [InlineData(2844, "Consumer & retail")]   // perfumes and cosmetics
    [InlineData(100, "Consumer & retail")]    // crops
    [InlineData(2834, "Healthcare")]
    public void Every_SIC_range_has_a_sector(int sic, string broad) => Assert.Equal(broad, SectorClassifier.FromSic(sic));

    [Fact]
    public void Every_classification_scheme_maps_to_a_broad_sector()
    {
        IndustryClassification K(string scheme, string? code, string name) => new() { Scheme = scheme, Code = code, Name = name, Source = "test" };
        Assert.Equal("Semiconductors", SectorClassifier.FromClassification(K("GICS", "4530", "Semiconductors & Semiconductor Equipment"), "Other"));
        Assert.Equal("Software & IT", SectorClassifier.FromClassification(K("SIC", "7372", "Services-Prepackaged Software"), "Other"));
        Assert.Equal("Finance", SectorClassifier.FromClassification(K("PSX", null, "Commercial Banks"), "Finance"));   // PSX keeps the import's sector
        Assert.Equal("Healthcare", SectorClassifier.FromClassification(K("ICB", null, "Health Care"), "Other"));
    }

    [Fact]
    public void A_reviewed_finding_stops_counting_and_a_stale_decision_is_reported()
    {
        var check = new CheckResult("Financials", "Negative revenue", Severity.Warning, "", 2, [],
            ["QLT.L (Quilter) FY2022: -1,234", "SDLF.L (Standard Life) FY2022: -5,678"]);
        var decisions = new[]
        {
            new AuditDecision("Negative revenue", "QLT.L", "FY2022", "real", "Policyholder investment losses (annual report p.180)", Today),
            new AuditDecision("Negative revenue", "NANO.PA", "", "real", "Gone since", Today)
        };
        var (audited, unused) = AuditLog.Apply([check], decisions);
        Assert.Equal(1, audited[0].Check.Count);
        Assert.Equal("SDLF.L (Standard Life) FY2022: -5,678", Assert.Single(audited[0].Check.All!));
        Assert.Equal("QLT.L", Assert.Single(audited[0].Reviewed).Decision.CompanyId);
        Assert.Equal("NANO.PA", Assert.Single(unused).CompanyId);
    }

    [Theory]
    [InlineData("AGX, ARG.PA — Argan Inc", "ARG.PA", true)]
    [InlineData("AGXX (Other) FY2022", "AGX", false)]
    [InlineData("HON (Honeywell) Q3 2026", "HON", true)]
    public void A_decision_covers_findings_that_name_its_company(string finding, string id, bool covered) =>
        Assert.Equal(covered, AuditLog.Covers(new AuditDecision("C", id, "", "real", "", null), "C", finding));

    [Theory]
    [InlineData(6221, "United States Oil Fund, LP", true)]
    [InlineData(6221, "Invesco CurrencyShares Euro Trust", true)]
    [InlineData(6221, "Invesco Galaxy Bitcoin ETF", true)]
    [InlineData(6221, "AIB Data Centers Inc.", false)]
    [InlineData(6798, "KBS Real Estate Investment Trust III, Inc.", false)]
    public void Exchange_traded_funds_are_not_companies(int sic, string name, bool fund) =>
        Assert.Equal(fund, ImportPipeline.IsExchangeTradedFund(sic, name));
}
