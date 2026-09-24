using System.Net;
using CompanyPaisa.Client;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Api.Tests;

/// <summary>Searching a whole country or state ("Utah", "US", "US-UT") instead of a radius.</summary>
public class RegionApiTests(SampleDataFactory factory) : IClassFixture<SampleDataFactory>
{
    private CompanyPaisaClient Client() => new(factory.CreateClient());

    [Fact]
    public async Task A_state_search_has_every_company_there_with_no_distances()
    {
        var utah = await Client().GetCompaniesInRegionAsync("US-UT");
        var nearLehi = await Client().GetCompaniesNearZipAsync("84043", radiusMiles: 10);

        Assert.Equal("US-UT", utah.Region?.Code);
        Assert.Equal("Utah", utah.OriginLabel);
        Assert.Equal(0, utah.RadiusMiles);
        Assert.All(utah.Items, c => Assert.Equal(0, c.DistanceMiles));
        Assert.All(utah.Items, c => Assert.Equal("UT", c.NearestLocation.State));
        Assert.Contains(utah.Items, c => c.Ticker == "LFVN" && c.IsHeadquarteredNearby);
        Assert.True(utah.TotalCount >= nearLehi.TotalCount);
        Assert.Equal(utah.TotalCount, utah.Summary.CompanyCount);
    }

    [Fact]
    public async Task Names_work_as_well_as_codes_in_region_and_near()
    {
        var byCode = await Client().GetCompaniesInRegionAsync("US-UT");
        var byName = await Client().GetCompaniesInRegionAsync("Utah");
        var byNear = await Client().GetCompaniesNearAsync(new NearbyCompaniesRequest { Near = "Utah", PageSize = 5000 });
        var country = await Client().GetCompaniesInRegionAsync("United States");

        Assert.Equal(byCode.TotalCount, byName.TotalCount);
        Assert.Equal("US-UT", byNear.Region?.Code);
        Assert.Equal(byCode.TotalCount, byNear.TotalCount);
        Assert.Equal(RegionKind.Country, country.Region?.Kind);
        Assert.True(country.TotalCount >= byCode.TotalCount);
    }

    [Fact]
    public async Task Executives_can_be_searched_by_region_too()
    {
        var result = await Client().GetExecutivesNearAsync(new ExecutivesNearRequest { Region = "US-UT" });
        Assert.Equal("US-UT", result.Region?.Code);
        Assert.NotEmpty(result.Items);
    }

    [Fact]
    public async Task The_lookup_answers_a_region_name_with_the_region()
    {
        var hit = await Client().LookupAsync("Utah");
        Assert.Equal("US-UT", hit?.Region?.Code);
        Assert.Equal("utah", hit?.Region?.Slug);
        Assert.Equal("", hit?.City);
        Assert.InRange(hit!.Point.Latitude, 37, 42);                 // the middle of Utah's companies
        Assert.Null((await Client().LookupAsync("84043"))?.Region);
    }

    [Fact]
    public async Task The_site_search_offers_the_place_the_text_names()
    {
        var utah = await Client().SearchByNameAsync("UTAH");
        var usa = await Client().SearchByNameAsync("USA");
        var lehi = await Client().SearchByNameAsync("Lehi");
        var company = await Client().SearchByNameAsync("lifevantage");

        Assert.Equal("utah", Assert.Single(utah.Places!).Place);
        Assert.Equal(RegionKind.Country, Assert.Single(usa.Places!).Region?.Kind);
        var city = Assert.Single(lehi.Places!);
        Assert.Null(city.Region);
        Assert.Equal("Lehi, UT", city.Place);
        Assert.Empty(company.Places!);
        Assert.NotEmpty(company.Companies);
    }

    [Fact]
    public async Task A_city_with_its_country_is_found_and_Washington_offers_the_city_and_the_state()
    {
        var lehi = await Client().LookupAsync("Lehi, USA");
        var washington = await Client().SearchByNameAsync("Washington");

        Assert.Equal("Lehi", lehi?.City);
        Assert.Null(lehi?.Region);
        Assert.Contains(washington.Places!, p => p.Region?.Code == "US-WA" && p.Place == "washington-state");
    }

    [Fact]
    public async Task An_unknown_region_is_a_bad_request()
    {
        var ex = await Assert.ThrowsAsync<CompanyPaisaApiException>(() => Client().GetCompaniesInRegionAsync("Atlantis"));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}

/// <summary>Region search pages as search engines and link previews get them (with a stand-in for the built website).</summary>
public class RegionPageTests(SitePagesFactory factory) : IClassFixture<SitePagesFactory>
{
    [Fact]
    public async Task Region_pages_are_rendered_for_search_engines_and_listed_in_the_sitemap()
    {
        var http = factory.CreateClient();
        var page = await http.GetStringAsync("/near/utah");
        var sitemap = await http.GetStringAsync("/sitemap-1.xml");     // the first file of the sitemap index: home, areas, regions

        Assert.Contains("<title>Public companies in Utah", page);
        Assert.Contains("<h1>Public companies in Utah</h1>", page);
        Assert.Contains("/near/utah</loc>", sitemap);
        Assert.Contains("/near/united-states</loc>", sitemap);
        Assert.DoesNotContain("/near/texas</loc>", sitemap);          // no sample companies there
    }
}
