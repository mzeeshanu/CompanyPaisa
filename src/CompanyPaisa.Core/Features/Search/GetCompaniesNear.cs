using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Features.Search;

/// <summary>Public companies with at least one location within a radius of a ZIP/city or coordinates.</summary>
public sealed record GetCompaniesNearQuery(NearbyCompaniesRequest Request) : IRequest<NearbyCompaniesResponse>, ICacheableRequest
{
    public string CacheKey => string.Create(CultureInfo.InvariantCulture,
        $"near|{Request.Near?.Trim().ToUpperInvariant()}|{Request.Latitude:F5}|{Request.Longitude:F5}|{Request.RadiusMiles}|{Request.Sector?.ToUpperInvariant()}|{Request.HeadquarteredOnly}|{Request.Sort}|{Request.Page}|{Request.PageSize}");
    public string CacheProfile => "Search";
}

public sealed class GetCompaniesNearValidator(IOptionsMonitor<SearchOptions> options) : IRequestValidator<GetCompaniesNearQuery>
{
    public IEnumerable<ValidationError> Validate(GetCompaniesNearQuery query)
    {
        var r = query.Request;
        var o = options.CurrentValue;
        var hasCoords = r.Latitude is not null || r.Longitude is not null;

        if (string.IsNullOrWhiteSpace(r.Near) && !hasCoords)
            yield return new("near", "Provide a ZIP code or city in 'near', or 'latitude' and 'longitude'.");
        if (hasCoords && (r.Latitude is null || r.Longitude is null))
            yield return new("latitude", "Provide both 'latitude' and 'longitude'.");
        if (r.Latitude is not null && r.Longitude is not null && !new GeoPoint(r.Latitude.Value, r.Longitude.Value).IsValid)
            yield return new("latitude", "Coordinates are out of range.");
        if (r.RadiusMiles is { } radius && (radius <= 0 || radius > o.MaxRadiusMiles))
            yield return new("radiusMiles", $"Radius must be greater than 0 and at most {o.MaxRadiusMiles} miles.");
        if (r.Page is < 1)
            yield return new("page", "Page starts at 1.");
        if (r.PageSize is { } size && (size < 1 || size > o.MaxPageSize))
            yield return new("pageSize", $"Page size must be between 1 and {o.MaxPageSize}.");
    }
}

public sealed class GetCompaniesNearHandler(
    ICompanyRepository repository,
    IGeoLocator geoLocator,
    IDistanceCalculator distance,
    IFinancialMetricsService metrics,
    IOptionsMonitor<SearchOptions> options) : IRequestHandler<GetCompaniesNearQuery, NearbyCompaniesResponse>
{
    public async Task<NearbyCompaniesResponse> HandleAsync(GetCompaniesNearQuery query, CancellationToken ct)
    {
        var r = query.Request;
        var o = options.CurrentValue;

        var (origin, originLabel) = await ResolveOriginAsync(r, ct);
        var radius = r.RadiusMiles ?? o.DefaultRadiusMiles;
        var sort = r.Sort ?? o.DefaultSort;
        var page = r.Page ?? 1;
        var pageSize = r.PageSize ?? o.DefaultPageSize;

        // 1. Cheap rectangle pre-filter, then exact distance; keep each company's nearest qualifying location.
        var candidates = await repository.GetLocationsWithinAsync(distance.BoundingBox(origin, radius), ct);
        var inRange = candidates
            .Select(l => (Location: l, Miles: distance.DistanceMiles(origin, l.Point)))
            .Where(x => x.Miles <= radius)
            .GroupBy(x => x.Location.CompanyId)
            .ToDictionary(g => g.Key, g => (
                Nearest: g.MinBy(x => x.Miles),
                HasHq: g.Any(x => x.Location.IsHeadquarters)));

        // 2. Company-level filters.
        var companies = (await repository.GetCompaniesAsync(inRange.Keys, ct))
            .Where(c => string.IsNullOrWhiteSpace(r.Sector) || string.Equals(c.Sector, r.Sector.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => !r.HeadquarteredOnly || inRange[c.CompanyId].HasHq)
            .ToList();

        // 3. Indicators for every match (summary covers the whole result, not just this page).
        var financials = await repository.GetFinancialsAsync(companies.Select(c => c.CompanyId), ct);
        var rows = companies.Select(c =>
        {
            var hit = inRange[c.CompanyId];
            var indicators = metrics.Compute(financials.TryGetValue(c.CompanyId, out var f) ? f : []);
            return new CompanySummaryDto(c.Ticker, c.Name, c.Exchange, c.Sector, hit.HasHq,
                hit.Nearest.Location.ToDto(), Math.Round(hit.Nearest.Miles, 2), indicators.ToDto());
        }).ToList();

        var summary = new NearbySummaryDto(
            rows.Count,
            rows.Sum(x => x.Indicators.TtmRevenue),
            rows.Count(x => x.Indicators.Trend == TrendStatus.Up),
            rows.Count(x => x.IsHeadquarteredNearby));

        var items = Sort(rows, sort).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new NearbyCompaniesResponse(origin.ToDto(), originLabel, radius, sort, page, pageSize, rows.Count, summary, items);
    }

    private async Task<(GeoPoint Point, string? Label)> ResolveOriginAsync(NearbyCompaniesRequest r, CancellationToken ct)
    {
        if (r.Latitude is { } lat && r.Longitude is { } lng) return (new GeoPoint(lat, lng), null);

        var hit = await geoLocator.LookupAsync(r.Near!, ct)
                  ?? throw new NotFoundException($"We couldn't find '{r.Near}'. Try a 5-digit ZIP code or 'City, ST'.");
        var label = hit.PostalCode is null ? $"{hit.City}, {hit.State}" : $"{hit.City}, {hit.State} {hit.PostalCode}";
        return (hit.Point, label);
    }

    private static IEnumerable<CompanySummaryDto> Sort(IEnumerable<CompanySummaryDto> rows, CompanySort sort) => sort switch
    {
        CompanySort.Growth => rows.OrderByDescending(x => x.Indicators.RevenueGrowthYoY ?? decimal.MinValue),
        CompanySort.Profit => rows.OrderByDescending(x => x.Indicators.TtmNetIncome),
        CompanySort.Distance => rows.OrderBy(x => x.DistanceMiles),
        _ => rows.OrderByDescending(x => x.Indicators.TtmRevenue)
    };
}
