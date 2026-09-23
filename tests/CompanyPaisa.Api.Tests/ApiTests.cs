using System.Net;
using System.Net.Http.Json;
using CompanyPaisa.Client;
using CompanyPaisa.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyPaisa.Api.Tests;

/// <summary>
/// End-to-end tests: hosts the real API (with the sample workbook) in memory and calls it
/// through the same <see cref="CompanyPaisaClient"/> other apps will use.
/// </summary>
public class ApiTests(SampleDataFactory factory) : IClassFixture<SampleDataFactory>
{
    private const string DevKey = "dev-only-key-change-me-0001";

    private CompanyPaisaClient Client(string? apiKey = null)
    {
        var http = factory.CreateClient();
        if (apiKey is not null) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return new CompanyPaisaClient(http);
    }

    [Fact]
    public async Task Companies_near_a_ZIP_code_include_LifeVantage()
    {
        var result = await Client().GetCompaniesNearZipAsync("84043", radiusMiles: 10);

        Assert.True(result.TotalCount > 5);
        var lfvn = Assert.Single(result.Items, c => c.Ticker == "LFVN");
        Assert.True(lfvn.IsHeadquarteredNearby);
        Assert.All(result.Items, c => Assert.True(c.DistanceMiles <= 10));
        Assert.Equal(result.Items.OrderByDescending(c => c.Indicators.TtmRevenue).Select(c => c.Ticker), result.Items.Select(c => c.Ticker));
    }

    [Fact]
    public async Task Company_profile_financials_and_executives_are_available()
    {
        var client = Client(DevKey);
        var company = await client.GetCompanyAsync("lfvn");
        var annual = await client.GetFinancialsAsync("LFVN", PeriodType.Annual, years: 3);
        var execs = await client.GetExecutivesAsync("LFVN");

        Assert.Equal("LifeVantage", company!.Name);
        Assert.Equal(3, annual!.Periods.Count);
        Assert.NotEmpty(execs!.Executives);
    }

    [Fact]
    public async Task Executives_near_a_ZIP_code_have_ten_years_of_pay_and_careers_across_companies()
    {
        var client = Client();
        var result = await client.GetExecutivesNearAsync(new ExecutivesNearRequest { Near = "84043", RadiusMiles = 25, Sort = ExecutiveSort.TotalPay });

        Assert.NotEmpty(result.Items);
        Assert.Equal(result.Items.OrderByDescending(e => e.WindowTotalPay).Select(e => e.PersonId), result.Items.Select(e => e.PersonId));
        Assert.All(result.Items, e => Assert.True(e.DistanceMiles <= 25));

        // The sample data has people who moved companies (e.g. into LifeVantage's CEO seat in 2021).
        var mover = Assert.Single(result.Items, e => e.Company.Ticker == "LFVN" && e.Title == "Chief Executive Officer");
        Assert.Equal(2, mover.CompanyCount);

        var career = await client.GetExecutiveAsync(mover.PersonId);
        Assert.Equal(["LFVN", "NUS"], career!.Roles.Select(r => r.Company.Ticker));
        Assert.Equal(career.History.Sum(h => h.Total), career.TotalPay);
    }

    [Fact]
    public async Task Unknown_ticker_returns_null_from_the_client() =>
        Assert.Null(await Client().GetCompanyAsync("NOPE"));

    [Fact]
    public async Task Invalid_input_returns_problem_details()
    {
        var ex = await Assert.ThrowsAsync<CompanyPaisaApiException>(() =>
            Client().GetCompaniesNearAsync(new NearbyCompaniesRequest { Near = "84043", RadiusMiles = 5000 }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task A_wrong_API_key_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<CompanyPaisaApiException>(() => Client("not-a-real-key-000000").GetMetaAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Client_config_comes_from_appsettings()
    {
        var config = await factory.CreateClient().GetFromJsonAsync<ClientConfigDto>("/api/v1/client-config", CompanyPaisaClientJson.Options);
        Assert.Equal([5, 10, 25, 50], config!.AllowedRadiiMiles);
        Assert.Equal("Auto", config.DefaultTheme);
        Assert.Contains(config.Coverage, c => c.Name == "Wasatch Front" && c.ExampleZip == "84043");
        Assert.Contains(config.Coverage, c => c.Name == "Rest of Minnesota");
        Assert.Contains(config.Coverage, c => c.Name == "London" && c.ExampleZip == "EC2N" && c.Country == "UK");
        Assert.Contains(config.Coverage, c => c.Name == "Rest of Utah");
        Assert.Contains(config.Coverage, c => c.Name == "Toronto" && c.ExampleZip == "M5J" && c.Country == "CA");
        Assert.Equal(132, config.Coverage.Count(c => c.Country == "US"));
        Assert.Contains(config.Coverage, c => c.Name == "Rest of Texas");
        Assert.Contains(config.Coverage, c => c.Name == "Northwest Arkansas" && c.ExampleZip == "72712");
        Assert.Contains(config.Coverage, c => c.Name == "Silicon Valley" && c.ExampleZip == "94086");
        Assert.Equal(6, config.Coverage.Count(c => c.Country == "CA"));
        Assert.Equal(10, config.Coverage.Count(c => c.Country == "UK"));
        Assert.Contains(config.Coverage, c => c.Name == "Paris" && c.ExampleZip == "75008" && c.Country == "FR");
        Assert.Equal(14, config.Coverage.Count(c => c.Country is "FR" or "NL" or "IT" or "ES"));
        Assert.Equal(7, config.Coverage.Count(c => c.Country is "AU" or "NZ"));
        Assert.Contains(config.Coverage, c => c.Name == "Karachi" && c.ExampleZip == "74000" && c.Country == "PK");
        Assert.Equal(5, config.Coverage.Count(c => c.Country == "PK"));
        Assert.Equal("privacy@companypaisa.com", config.PrivacyContact);
    }

    [Theory]
    [InlineData("M5J", "Toronto", "ON")]          // Canadian postal area (no UK district called M5J)
    [InlineData("m5j 2j2", "Toronto", "ON")]      // full Canadian postal code
    [InlineData("V6C 1A1", "Vancouver", "BC")]
    [InlineData("M2", "Manchester", "UK")]        // UK district
    [InlineData("N1C", "London", "UK")]           // exact UK district wins over the Canadian FSA of the same name
    [InlineData("EC2A 1NQ", "London", "UK")]      // UK fine district falls back to EC2
    [InlineData("FR-75008", "Paris", "FR")]       // European codes come with their country
    [InlineData("NL-1012 AB", "Amsterdam", "NL")]
    [InlineData("IT 20121", "Milano", "IT")]
    [InlineData("ES-08002", "Barcelona", "ES")]
    [InlineData("AU-2000", "Sydney", "AU")]
    [InlineData("NZ 1010", "Auckland", "NZ")]
    [InlineData("PK-74000", "Karachi", "PK")]      // a bare 74000 would be a US ZIP
    [InlineData("PK 54000", "Lahore", "PK")]
    public async Task Postcodes_resolve_to_the_right_country(string query, string city, string state)
    {
        var hit = await Client().LookupAsync(query);
        Assert.NotNull(hit);
        Assert.Equal(state, hit!.State);
        Assert.Contains(city, hit.City);
    }

    /// <summary>A bare 5-digit code is only ever a US ZIP — never a French, Italian or Spanish postcode.</summary>
    [Fact]
    public async Task A_bare_five_digit_code_is_not_looked_up_in_europe()
    {
        var hit = await Client().LookupAsync("75008");   // Paris 8e, and also a US ZIP (Carrollton, TX)
        Assert.True(hit is null || hit.State == "TX");
    }

    /// <summary>The website asks for every company in the radius; 200 used to be the most the API would return.</summary>
    [Fact]
    public async Task A_search_can_return_more_than_200_companies_in_one_page()
    {
        var result = await Client().GetCompaniesNearAsync(new NearbyCompaniesRequest { Near = "84043", RadiusMiles = 50, PageSize = 2000 });
        Assert.Equal(result.TotalCount, result.Items.Count);
    }

    [Theory]
    [InlineData("CF-Connecting-IP", "203.0.113.7", "203.0.113.7")]   // Cloudflare's header: the visitor
    [InlineData("CF-Connecting-IP", null, "10.0.0.1")]               // no header (direct to the host): the connection
    [InlineData("CF-Connecting-IP", "not-an-ip", "10.0.0.1")]        // junk is ignored
    [InlineData(null, "203.0.113.7", "10.0.0.1")]                    // not configured: the header isn't trusted
    public void Rate_limits_use_the_visitors_ip_behind_a_cdn(string? header, string? value, string expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        if (value is not null) context.Request.Headers["CF-Connecting-IP"] = value;
        Assert.Equal(expected, CompanyPaisa.Api.Security.ApiKeyValidator.ClientIp(context, header));
    }

    [Fact]
    public void A_cached_search_costs_one_unit_per_row()
    {
        var items = Enumerable.Range(0, 37).Select(_ => (CompanySummaryDto)null!).ToList();
        var response = new NearbyCompaniesResponse(new GeoPointDto(0, 0), null, 10, CompanySort.Revenue, 1, 50, 37, null!, items);
        Assert.Equal(37, CompanyPaisa.Infrastructure.Messaging.CacheWeight.Of(response));
        Assert.Equal(1, CompanyPaisa.Infrastructure.Messaging.CacheWeight.Of(new object()));
    }

    [Fact]
    public async Task Health_and_openapi_are_exposed()
    {
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/openapi/v1.json")).StatusCode);
    }
}

internal static class CompanyPaisaClientJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
