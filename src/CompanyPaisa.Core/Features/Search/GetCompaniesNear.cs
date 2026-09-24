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

/// <summary>Public companies with at least one location within a radius of a ZIP/city or coordinates, or in a whole country or state.</summary>
public sealed record GetCompaniesNearQuery(NearbyCompaniesRequest Request) : IRequest<NearbyCompaniesResponse>, ICacheableRequest, ITrackedRequest<NearbyCompaniesResponse>
{
    public string CacheKey => string.Create(CultureInfo.InvariantCulture,
        $"near|{Request.Near?.Trim().ToUpperInvariant()}|{Request.Region?.Trim().ToUpperInvariant()}|{Request.Latitude:F3}|{Request.Longitude:F3}|{Request.RadiusMiles}|{Request.Sector?.ToUpperInvariant()}|{Request.HeadquarteredOnly}|{Request.Sort}|{Request.Reverse}|{Request.Search?.Trim().ToUpperInvariant()}|{Request.IncludeBubbles}|{Request.Page}|{Request.PageSize}");
    public string CacheProfile => "Search";

    /// <summary>
    /// The first page only, and not the website's one-row "is there anything here?" check on the location screen
    /// (<see cref="NearbyCompaniesRequest.PageSize"/> 1).
    /// </summary>
    public AnalyticsAction? Describe(NearbyCompaniesResponse response) => (Request.Page ?? 1) != 1 || Request.PageSize == 1 ? null :
        SearchAnalytics.Action("search", response.Origin, response.OriginLabel, response.RadiusMiles, Request.Near, Request.Sector,
            response.TotalCount, ("headquarteredOnly", Request.HeadquarteredOnly ? "true" : null), ("sort", Request.Sort?.ToString()),
            ("region", response.Region?.Code));
}

/// <summary>How a "near me" search is recorded: where (rounded to ~1 km), how far, which filters, how many results.</summary>
public static class SearchAnalytics
{
    /// <summary>Two decimal places ≈ 1.1 km: enough to tell areas apart, too coarse to pinpoint a home.</summary>
    public const int CoordinateDecimals = 2;

    public static AnalyticsAction Action(string name, GeoPointDto origin, string? originLabel, double radiusMiles, string? near, string? sector,
        int results, params (string Key, string? Value)[] extra)
    {
        var detail = new Dictionary<string, string?>
        {
            ["radiusMiles"] = radiusMiles.ToString(CultureInfo.InvariantCulture),
            ["results"] = results.ToString(CultureInfo.InvariantCulture),
            ["near"] = string.IsNullOrWhiteSpace(near) ? null : near.Trim(),
            ["sector"] = string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(),
        };
        foreach (var (key, value) in extra) detail[key] = string.IsNullOrWhiteSpace(value) ? null : value;
        return new AnalyticsAction(name, null, originLabel, detail,
            Math.Round(origin.Latitude, CoordinateDecimals), Math.Round(origin.Longitude, CoordinateDecimals));
    }
}

public sealed class GetCompaniesNearValidator(IOptionsMonitor<SearchOptions> options) : IRequestValidator<GetCompaniesNearQuery>
{
    public IEnumerable<ValidationError> Validate(GetCompaniesNearQuery query)
    {
        var r = query.Request;
        var errors = NearbyValidation.Validate(r.Near, r.Region, r.Latitude, r.Longitude, r.RadiusMiles, r.Page, r.PageSize, options.CurrentValue);
        return r.Search is { Length: > 100 } ? errors.Append(new("search", "Search text is too long.")) : errors;
    }
}

/// <summary>Location / radius / paging rules shared by every "near me" search.</summary>
public static class NearbyValidation
{
    public static IEnumerable<ValidationError> Validate(string? near, string? region, double? latitude, double? longitude, double? radiusMiles,
        int? page, int? pageSize, SearchOptions o)
    {
        var hasCoords = latitude is not null || longitude is not null;
        if (!string.IsNullOrWhiteSpace(region) && Regions.FromCode(region) is null && Regions.Find(region) is null)
            yield return new("region", "Unknown region. Use a country or state code (US, UK, US-TX, CA-ON) or its name.");
        if (string.IsNullOrWhiteSpace(near) && string.IsNullOrWhiteSpace(region) && !hasCoords)
            yield return new("near", "Provide a ZIP code or city in 'near', a country or state in 'region', or 'latitude' and 'longitude'.");
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

        var sort = r.Sort ?? o.DefaultSort;
        var page = r.Page ?? 1;
        var pageSize = r.PageSize ?? o.DefaultPageSize;

        // 1. Companies with a location in range (or in the country / state), each with its nearest qualifying location.
        var area = await nearby.FindAreaAsync(r.Near, r.Region, r.Latitude, r.Longitude, r.RadiusMiles ?? o.DefaultRadiusMiles, ct);
        var inRange = area.Hits;

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

        // Every company as a bubble (when asked), then the list: narrowed by the search, sorted, one page of it.
        var bubbles = r.IncludeBubbles
            ? rows.Select(x => new CompanyBubbleDto(x.Ticker, x.Name, x.Indicators.TtmRevenue, x.Indicators.RevenueGrowthYoY, x.Indicators.Trend,
                x.NearestLocation.City, x.NearestLocation.State, x.DistanceMiles, x.IsHeadquarteredNearby, x.Currency)).ToList()
            : null;
        var listed = Matching(rows, r.Search).ToList();
        var items = Sort(listed, sort, r.Reverse).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new NearbyCompaniesResponse(area.Origin.ToDto(), area.Label, area.RadiusMiles, sort, page, pageSize, listed.Count, summary, items,
            area.Region?.ToDto(), bubbles);
    }

    /// <summary>Names containing the text (ignoring case and accents: "societe" finds Société Générale) or tickers starting with it.</summary>
    private static IEnumerable<CompanySummaryDto> Matching(IEnumerable<CompanySummaryDto> rows, string? search)
    {
        var q = Plain(search?.Trim() ?? "");
        return q.Length == 0 ? rows
            : rows.Where(x => Plain(x.Name).Contains(q, StringComparison.Ordinal) || x.Ticker.StartsWith(q, StringComparison.OrdinalIgnoreCase));
    }

    private static string Plain(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    /// <summary>
    /// Biggest first (nearest first for distance), or the opposite when reversed; companies with no growth figure stay last
    /// either way. Ties go by ticker so pages never overlap.
    /// </summary>
    private IEnumerable<CompanySummaryDto> Sort(IEnumerable<CompanySummaryDto> rows, CompanySort sort, bool reverse)
    {
        var ordered = sort switch
        {
            CompanySort.Growth => reverse
                ? rows.OrderBy(x => x.Indicators.RevenueGrowthYoY is null).ThenBy(x => x.Indicators.RevenueGrowthYoY)
                : rows.OrderByDescending(x => x.Indicators.RevenueGrowthYoY ?? decimal.MinValue),
            CompanySort.Profit => Direction(rows, x => fx.ToUsd(x.Indicators.TtmNetIncome, x.Currency), !reverse),
            CompanySort.Distance => Direction(rows, x => (decimal)x.DistanceMiles, reverse),
            _ => Direction(rows, x => fx.ToUsd(x.Indicators.TtmRevenue, x.Currency), !reverse)
        };
        return ordered.ThenBy(x => x.Ticker, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<CompanySummaryDto> Direction(IEnumerable<CompanySummaryDto> rows, Func<CompanySummaryDto, decimal> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
}
