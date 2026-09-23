using CompanyPaisa.Api.Options;
using CompanyPaisa.Api.Security;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Features.Companies;
using CompanyPaisa.Core.Features.Executives;
using CompanyPaisa.Core.Features.Reference;
using CompanyPaisa.Core.Features.Search;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Endpoints;

/// <summary>
/// Public API v1. Endpoints only translate HTTP ⇄ requests; all logic lives in Core handlers.
/// </summary>
public static class V1Endpoints
{
    public const string RateLimitPolicy = "api";

    public static IEndpointRouteBuilder MapCompanyPaisaApiV1(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1")
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("CompanyPaisa v1");

        // ----- Search -----
        v1.MapGet("/companies/near", async (
                string? near, double? latitude, double? longitude, double? radiusMiles, string? sector,
                bool? headquarteredOnly, string? sort, int? page, int? pageSize,
                IServiceRequestor requestor, CancellationToken ct) =>
            {
                var request = new NearbyCompaniesRequest
                {
                    Near = near, Latitude = latitude, Longitude = longitude, RadiusMiles = radiusMiles, Sector = sector,
                    HeadquarteredOnly = headquarteredOnly ?? false, Sort = ParseEnum<CompanySort>(sort, "sort"),
                    Page = page, PageSize = pageSize
                };
                return Results.Ok(await requestor.SendAsync(new GetCompaniesNearQuery(request), ct));
            })
            .WithName("GetCompaniesNear")
            .WithSummary("Public companies with a location within a radius of a ZIP code, city or coordinates.")
            .WithDescription("Examples: ?near=84043  ·  ?near=Lehi, UT&radiusMiles=25  ·  ?latitude=40.39&longitude=-111.85&sort=growth")
            .Produces<NearbyCompaniesResponse>();

        // ----- Companies -----
        v1.MapGet("/companies/{ticker}", async (string ticker, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new GetCompanyQuery(ticker), ct)))
            .WithName("GetCompany").WithSummary("Company profile, locations and headline indicators.")
            .Produces<CompanyDetailDto>();

        v1.MapGet("/companies/{ticker}/financials", async (string ticker, string? period, int? years, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(
                    new GetFinancialsQuery(ticker, ParseEnum<PeriodType>(period, "period") ?? PeriodType.Quarterly, years), ct)))
            .WithName("GetFinancials").WithSummary("Quarterly or annual revenue and net income, with year-over-year growth.")
            .Produces<FinancialsResponse>();

        v1.MapGet("/companies/{ticker}/executives", async (string ticker, int? years, IServiceRequestor requestor, IOptionsMonitor<FeatureOptions> features, CancellationToken ct) =>
                features.CurrentValue.IsEnabled("Executives")
                    ? Results.Ok(await requestor.SendAsync(new GetExecutivesQuery(ticker, years), ct))
                    : Results.NotFound())
            .WithName("GetExecutives").WithSummary("Executive compensation by year (salary, bonus, stock, other, total).")
            .Produces<ExecutivesResponse>();

        v1.MapGet("/companies/{ticker}/salaries", async (string ticker, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new GetJobSalariesQuery(ticker), ct)))
            .WithName("GetJobSalaries")
            .WithSummary("Yearly salaries the company offered by job title, company-wide and per work place, from its US H-1B wage filings.")
            .WithDescription("Titles with at least three filings; the 25th percentile, median and 75th percentile of the salary offered.")
            .Produces<JobSalariesResponse>();

        v1.MapGet("/companies/{ticker}/insights", async (string ticker, IServiceRequestor requestor, IOptionsMonitor<FeatureOptions> features, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new GetCompanyInsightsQuery(ticker, features.CurrentValue.IsEnabled("Executives")), ct)))
            .WithName("GetCompanyInsights")
            .WithSummary("Facts worked out from the company's figures (ranks, streaks, records, CEO pay vs results, margin vs sector) and similar companies nearby.")
            .Produces<CompanyInsightsResponse>();

        // ----- Executives (people) -----
        v1.MapGet("/executives/near", async (
                string? near, double? latitude, double? longitude, double? radiusMiles, string? sector, bool? includeFormer,
                string? search, string? role, string? sort, int? years, int? page, int? pageSize,
                IServiceRequestor requestor, IOptionsMonitor<FeatureOptions> features, CancellationToken ct) =>
            {
                if (!features.CurrentValue.IsEnabled("Executives")) return Results.NotFound();
                var request = new ExecutivesNearRequest
                {
                    Near = near, Latitude = latitude, Longitude = longitude, RadiusMiles = radiusMiles, Sector = sector,
                    IncludeFormer = includeFormer ?? false, Search = search, Role = ParseEnum<ExecutiveRole>(role, "role"), Sort = ParseEnum<ExecutiveSort>(sort, "sort"),
                    Years = years, Page = page, PageSize = pageSize
                };
                return Results.Ok(await requestor.SendAsync(new GetExecutivesNearQuery(request), ct));
            })
            .WithName("GetExecutivesNear")
            .WithSummary("Named executive officers of public companies near a ZIP code, city or coordinates, with up to 10 years of pay.")
            .WithDescription("Examples: ?near=84043  ·  ?near=84043&sort=TotalPay&years=10  ·  ?near=84043&includeFormer=true&search=financial  ·  ?near=94105&role=Ceo")
            .Produces<ExecutivesNearResponse>();

        v1.MapGet("/executives/{personId}", async (string personId, IServiceRequestor requestor, IOptionsMonitor<FeatureOptions> features, CancellationToken ct) =>
                features.CurrentValue.IsEnabled("Executives")
                    ? Results.Ok(await requestor.SendAsync(new GetExecutiveQuery(personId), ct))
                    : Results.NotFound())
            .WithName("GetExecutive")
            .WithSummary("One executive's career and pay history across every company they were a named executive officer at.")
            .Produces<ExecutiveDetailDto>();

        // ----- Search by name -----
        v1.MapGet("/search", async (string? q, int? limit, IServiceRequestor requestor, IOptionsMonitor<FeatureOptions> features, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new SearchByNameQuery(q ?? "", limit, features.CurrentValue.IsEnabled("Executives")), ct)))
            .WithName("SearchByName")
            .WithSummary("Companies (by name or ticker) and executives (by name) anywhere in the data set, best matches first.")
            .WithDescription("Examples: ?q=nvidia  ·  ?q=NVDA  ·  ?q=tim cook&limit=3")
            .Produces<NameSearchResponse>();

        // ----- Reference -----
        v1.MapGet("/geo/lookup", async (string q, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new LookupGeoQuery(q), ct)))
            .WithName("LookupGeo").WithSummary("Resolve a US ZIP code, Canadian or UK postcode, or 'City, ST' to coordinates.")
            .Produces<GeoLookupDto>();

        v1.MapGet("/geo/zip/{zip}", async (string zip, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new LookupGeoQuery(zip), ct)))
            .WithName("LookupZip").WithSummary("Resolve a ZIP code to city and coordinates.")
            .Produces<GeoLookupDto>();

        v1.MapGet("/sectors", async (IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new GetSectorsQuery(), ct)))
            .WithName("GetSectors").WithSummary("All sectors in the directory.")
            .Produces<IReadOnlyList<string>>();

        v1.MapGet("/meta", async (IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new GetDataMetaQuery(), ct)))
            .WithName("GetMeta").WithSummary("Data version, as-of date, and whether the data is sample data.")
            .Produces<DataMetaDto>();

        v1.MapGet("/client-config", (IOptionsMonitor<UiOptions> ui, IOptionsMonitor<SearchOptions> search, IOptionsMonitor<FeatureOptions> features) =>
                Results.Ok(ui.CurrentValue.ToClientConfig(search.CurrentValue, features.CurrentValue)))
            .WithName("GetClientConfig").WithSummary("Website settings from appsettings.json (theme, radius options, features…).")
            .Produces<ClientConfigDto>();

        return app;
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)) return parsed;
        throw new RequestValidationException([new ValidationError(field, $"Use one of: {string.Join(", ", Enum.GetNames<TEnum>())}.")]);
    }
}
