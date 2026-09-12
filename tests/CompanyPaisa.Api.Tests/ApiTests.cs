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
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
        Assert.Equal("List", config.DefaultView);
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
