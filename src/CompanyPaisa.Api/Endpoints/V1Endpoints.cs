using CompanyPaisa.Api.Options;
using CompanyPaisa.Api.Security;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Features.Companies;
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

        // ----- Reference -----
        v1.MapGet("/geo/lookup", async (string q, IServiceRequestor requestor, CancellationToken ct) =>
                Results.Ok(await requestor.SendAsync(new LookupGeoQuery(q), ct)))
            .WithName("LookupGeo").WithSummary("Resolve a ZIP code or 'City, ST' to coordinates.")
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
            .WithName("GetClientConfig").WithSummary("Website settings from appsettings.json (default view, radius options, features…).")
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
