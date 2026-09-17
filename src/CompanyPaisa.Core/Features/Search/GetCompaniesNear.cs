using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Features.Search;

/// <summary>Public companies with at least one location within a radius of a ZIP/city or coordinates.</summary>
public sealed record GetCompaniesNearQuery(NearbyCompaniesRequest Request) : IRequest<NearbyCompaniesResponse>, ICacheableRequest
{
    public string CacheKey => string.Create(CultureInfo.InvariantCulture,
        $"near|{Request.Near?.Trim().ToUpperInvariant()}|{Request.Latitude:F3}|{Request.Longitude:F3}|{Request.RadiusMiles}|{Request.Sector?.ToUpperInvariant()}|{Request.HeadquarteredOnly}|{Request.Sort}|{Request.Page}|{Request.PageSize}");
    public string CacheProfile => "Search";
}

public sealed class GetCompaniesNearValidator(IOptionsMonitor<SearchOptions> options) : IRequestValidator<GetCompaniesNearQuery>
{
    public IEnumerable<ValidationError> Validate(GetCompaniesNearQuery query)
    {
        var r = query.Request;
        return NearbyValidation.Validate(r.Near, r.Latitude, r.Longitude, r.RadiusMiles, r.Page, r.PageSize, options.CurrentValue);
    }
}

/// <summary>Location / radius / paging rules shared by every "near me" search.</summary>
public static class NearbyValidation
{
    public static IEnumerable<ValidationError> Validate(string? near, double? latitude, double? longitude, double? radiusMiles,
        int? page, int? pageSize, SearchOptions o)
    {
        var hasCoords = latitude is not null || longitude is not null;
        if (string.IsNullOrWhiteSpace(near) && !hasCoords)
            yield return new("near", "Provide a ZIP code or city in 'near', or 'latitude' and 'longitude'.");
        if (hasCoords && (latitude is null || longitude is null))
            yield return new("latitude", "Provide both 'latitude' and 'longitude'.");
        if (latitude is not null && longitude is not null && !new GeoPoint(latitude.Value, longitude.Value).IsValid)
            yield return new("latitude", "Coordinates are out of range.");
        if (radiusMiles is { } radius && (radius <= 0 || radius > o.MaxRadiusMiles))
            yield return new("radiusMiles", $"Radius must be greater than 0 and at most {o.MaxRadiusMiles} miles.");
        if (page is < 1)
            yield return new("page", "Page starts at 1.");
        if (pageSize is { } size && (size < 1 || size > o.MaxPageSize))
            yield return new("pageSize", $"Page size must be between 1 and {o.MaxPageSize}.");
    }
}

public sealed class GetCompaniesNearHandler(
    ICompanyRepository repository,
    INearbySearchService nearby,
    IFinancialMetricsService metrics,
    ICurrencyConverter fx,
    ITopPaidCeoService topCeo,
    IOptionsMonitor<SearchOptions> options) : IRequestHandler<GetCompaniesNearQuery, NearbyCompaniesResponse>
{
    public async Task<NearbyCompaniesResponse> HandleAsync(GetCompaniesNearQuery query, CancellationToken ct)
    {
        var r = query.Request;
        var o = options.CurrentValue;

        var (origin, originLabel) = await nearby.ResolveOriginAsync(r.Near, r.Latitude, r.Longitude, ct);
        var radius = r.RadiusMiles ?? o.DefaultRadiusMiles;
        var sort = r.Sort ?? o.DefaultSort;
        var page = r.Page ?? 1;
        var pageSize = r.PageSize ?? o.DefaultPageSize;

        // 1. Companies with a location in range, each with its nearest qualifying location.
        var inRange = await nearby.FindCompaniesAsync(origin, radius, ct);

        // 2. Company-level filters.
        var companies = (await repository.GetCompaniesAsync(inRange.Keys, ct))
            .Where(c => string.IsNullOrWhiteSpace(r.Sector) || string.Equals(c.Sector, r.Sector.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(c => !r.HeadquarteredOnly || inRange[c.CompanyId].HasHeadquartersInRange)
            .ToList();

        // 3. Indicators for every match (summary covers the whole result, not just this page).
        var financials = await repository.GetFinancialsAsync(companies.Select(c => c.CompanyId), ct);
        var rows = companies.Select(c =>
        {
            var hit = inRange[c.CompanyId];
            var indicators = metrics.Compute(financials.TryGetValue(c.CompanyId, out var f) ? f : []);
            return new CompanySummaryDto(c.Ticker, c.Name, c.Exchange, c.Sector, hit.HasHeadquartersInRange,
                hit.NearestLocation.ToDto(), hit.DistanceMiles, indicators.ToDto(), c.Currency);
        }).ToList();

        // For the "quick fact" line: the best-paid CEO among the companies based here (not those with just an office in range).
        var ceo = await topCeo.FindAsync(companies.Where(c => inRange[c.CompanyId].HasHeadquartersInRange).ToList(), ct);

        // A search can mix currencies (London: Shell reports in dollars, Tesco in pounds) — add up in the main one.
        var currency = fx.Dominant(rows.Select(x => x.Currency));
        var summary = new NearbySummaryDto(
            rows.Count,
            rows.Sum(x => fx.Convert(x.Indicators.TtmRevenue, x.Currency, currency)),
            rows.Count(x => x.Indicators.Trend == TrendStatus.Up),
            rows.Count(x => x.IsHeadquarteredNearby),
            currency,
            rows.Any(x => !string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase)),
            ceo);

        var items = Sort(rows, sort).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new NearbyCompaniesResponse(origin.ToDto(), originLabel, radius, sort, page, pageSize, rows.Count, summary, items);
    }

    private IEnumerable<CompanySummaryDto> Sort(IEnumerable<CompanySummaryDto> rows, CompanySort sort) => sort switch
    {
        CompanySort.Growth => rows.OrderByDescending(x => x.Indicators.RevenueGrowthYoY ?? decimal.MinValue),
        CompanySort.Profit => rows.OrderByDescending(x => fx.ToUsd(x.Indicators.TtmNetIncome, x.Currency)),
        CompanySort.Distance => rows.OrderBy(x => x.DistanceMiles),
        _ => rows.OrderByDescending(x => fx.ToUsd(x.Indicators.TtmRevenue, x.Currency))
    };
}
