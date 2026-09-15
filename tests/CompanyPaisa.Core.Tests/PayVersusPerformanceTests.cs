using CompanyPaisa.Importer.Compensation;

namespace CompanyPaisa.Core.Tests;

public class PayVersusPerformanceTests
{
    /// <summary>Shaped like Apple's 2026 proxy: one CEO, totals tagged without a person, the name tagged per person.</summary>
    private const string OneCeo = """
        <ix:header><ix:resources>
          <xbrli:context id="FY25"><xbrli:entity><xbrli:identifier scheme="http://www.sec.gov/CIK">0000320193</xbrli:identifier></xbrli:entity>
            <xbrli:period><xbrli:startDate>2024-09-29</xbrli:startDate><xbrli:endDate>2025-09-27</xbrli:endDate></xbrli:period></xbrli:context>
          <xbrli:context id="FY25_Cook"><xbrli:entity><xbrli:identifier scheme="http://www.sec.gov/CIK">0000320193</xbrli:identifier>
            <xbrli:segment><xbrldi:explicitMember dimension="ecd:ExecutiveCategoryAxis">ecd:PeoMember</xbrldi:explicitMember>
            <xbrldi:explicitMember dimension="ecd:IndividualAxis">AAPL:CookMember</xbrldi:explicitMember></xbrli:segment></xbrli:entity>
            <xbrli:period><xbrli:startDate>2024-09-29</xbrli:startDate><xbrli:endDate>2025-09-27</xbrli:endDate></xbrli:period></xbrli:context>
        </ix:resources></ix:header>
        <td><ix:nonFraction name="ecd:PeoTotalCompAmt" contextRef="FY25" format="ixt:numdotdecimal" decimals="0" unitRef="USD">74,294,811</ix:nonFraction></td>
        <td><ix:nonNumeric contextRef="FY25_Cook" name="ecd:PeoName">Mr. Cook</ix:nonNumeric></td>
        """;

    [Fact]
    public void Reads_the_ceo_total_and_name()
    {
        var fact = Assert.Single(PayVersusPerformance.Read(OneCeo));
        Assert.Equal((2025, 74_294_811m, "Cook"), (fact.FiscalYear, fact.Total, fact.Name));
    }

    /// <summary>A year with two CEOs: each total sits in its own person context, in thousands (scale 3).</summary>
    private const string TwoCeos = """
        <xbrli:context id="A"><xbrli:entity><xbrli:segment><xbrldi:explicitMember dimension="ecd:IndividualAxis">x:SmithMember</xbrldi:explicitMember></xbrli:segment></xbrli:entity>
          <xbrli:period><xbrli:startDate>2024-01-01</xbrli:startDate><xbrli:endDate>2024-12-31</xbrli:endDate></xbrli:period></xbrli:context>
        <xbrli:context id="B"><xbrli:entity><xbrli:segment><xbrldi:explicitMember dimension="ecd:IndividualAxis">x:JonesMember</xbrldi:explicitMember></xbrli:segment></xbrli:entity>
          <xbrli:period><xbrli:startDate>2024-01-01</xbrli:startDate><xbrli:endDate>2024-12-31</xbrli:endDate></xbrli:period></xbrli:context>
        <ix:nonFraction name="ecd:PeoTotalCompAmt" contextRef="A" scale="3" unitRef="USD">5,100</ix:nonFraction>
        <ix:nonFraction name="ecd:PeoTotalCompAmt" contextRef="B" scale="3" unitRef="USD"><span>1,250</span></ix:nonFraction>
        <ix:nonNumeric name="ecd:PeoName" contextRef="A">Jane Smith</ix:nonNumeric>
        <ix:nonNumeric name="ecd:PeoName" contextRef="B">Robert Jones</ix:nonNumeric>
        """;

    [Fact]
    public void Separates_two_ceos_in_one_year()
    {
        var facts = PayVersusPerformance.Read(TwoCeos).OrderBy(f => f.Name).ToList();
        Assert.Equal([("Jane Smith", 5_100_000m), ("Robert Jones", 1_250_000m)], facts.Select(f => (f.Name, f.Total)));
    }

    [Fact]
    public void A_proxy_without_the_table_yields_nothing() =>
        Assert.Empty(PayVersusPerformance.Read("<p>Summary Compensation Table</p>"));

    [Theory]
    [InlineData("Cook", "Timothy D. Cook", true)]
    [InlineData("Tim Cook", "Timothy Donald Cook", true)]
    [InlineData("Robert L. Smith, Jr.", "Robert Smith", true)]
    [InlineData("Cook", "Luca Maestri", false)]
    public void Matches_names_by_surname(string pvp, string table, bool same) =>
        Assert.Equal(same, PayVersusPerformance.SamePerson(pvp, table));
}
