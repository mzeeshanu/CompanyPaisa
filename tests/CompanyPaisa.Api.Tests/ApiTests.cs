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
        Assert.Equal("List", config.DefaultView);
        Assert.Contains(config.Coverage, c => c.Name == "Wasatch Front" && c.ExampleZip == "84043");
        Assert.Contains(config.Coverage, c => c.Name == "Rest of Minnesota");
        Assert.Contains(config.Coverage, c => c.Name == "London" && c.ExampleZip == "EC2N" && c.Country == "UK");
        Assert.Contains(config.Coverage, c => c.Name == "Rest of Utah");
        Assert.Equal(23, config.Coverage.Count(c => c.Country == "US"));
        Assert.Equal(10, config.Coverage.Count(c => c.Country == "UK"));
        Assert.Equal("privacy@companypaisa.com", config.PrivacyContact);
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
