using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <inheritdoc cref="INearbySearchService"/>
public sealed class NearbySearchService(ICompanyRepository repository, IGeoLocator geoLocator, IDistanceCalculator distance)
    : INearbySearchService
{
    /// <summary>Decimal places kept from a caller's coordinates (3 ≈ 110 m). Cache keys use the same precision.</summary>
    public const int CoordinateDecimals = 3;

    public async Task<(GeoPoint Point, string? Label)> ResolveOriginAsync(string? near, double? latitude, double? longitude, CancellationToken ct = default)
    {
        // Coordinates are rounded to ~100 m: distances move by at most ~0.05 mi, and neighbours share cached results
        // (a phone's exact position would otherwise make almost every search a new cache entry).
        if (latitude is { } lat && longitude is { } lng) return (new GeoPoint(Math.Round(lat, CoordinateDecimals), Math.Round(lng, CoordinateDecimals)), null);

        var hit = await geoLocator.LookupAsync(near ?? "", ct)
                  ?? throw new NotFoundException($"We couldn't find '{near}'. Try a 5-digit ZIP code or 'City, ST'.");
        var place = string.IsNullOrWhiteSpace(hit.City) ? hit.State : $"{hit.City}, {hit.State}";
        var label = hit.PostalCode is null ? place : $"{place} {hit.PostalCode}";
        return (hit.Point, label);
    }

    /// <summary>Every location on Earth: region searches scan them all (about 5,000, all in memory).</summary>
    private static readonly GeoBoundingBox World = new(-90, 90, -180, 180);

    public async Task<IReadOnlyDictionary<string, NearbyCompanyHit>> FindCompaniesInRegionAsync(Region region, CancellationToken ct = default)
    {
        var inRegion = (await repository.GetLocationsWithinAsync(World, ct)).Where(region.Contains);
        return inRegion
            .GroupBy(l => l.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g =>
            {
                var shown = g.OrderByDescending(l => l.IsHeadquarters).ThenBy(l => l.Label, StringComparer.Ordinal).First();
                return new NearbyCompanyHit(g.Key, shown, 0, g.Any(l => l.IsHeadquarters));
            }, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<SearchArea> FindAreaAsync(string? near, string? region, double? latitude, double? longitude, double radiusMiles, CancellationToken ct = default)
    {
        var area = Regions.FromCode(region) ?? Regions.Find(region) ?? (latitude is null && longitude is null ? Regions.Find(near) : null);
        if (area is not null)
        {
            var hits = await FindCompaniesInRegionAsync(area, ct);
            return new SearchArea(Centre(hits.Values.Select(h => h.NearestLocation.Point)), area.Name, 0, area, hits);
        }
        var (origin, label) = await ResolveOriginAsync(near, latitude, longitude, ct);
        return new SearchArea(origin, label, radiusMiles, null, await FindCompaniesAsync(origin, radiusMiles, ct));
    }

    /// <summary>The middle of a region's companies (for the search's origin and the analytics); 0,0 when it has none.</summary>
    public static GeoPoint Centre(IEnumerable<GeoPoint> points)
    {
        var list = points.ToList();
        return list.Count == 0 ? new GeoPoint(0, 0)
            : new GeoPoint(Math.Round(list.Average(p => p.Latitude), CoordinateDecimals), Math.Round(list.Average(p => p.Longitude), CoordinateDecimals));
    }

    public async Task<IReadOnlyDictionary<string, NearbyCompanyHit>> FindCompaniesAsync(GeoPoint origin, double radiusMiles, CancellationToken ct = default)
    {
        // Cheap rectangle pre-filter, then exact distance; keep each company's nearest qualifying location.
        var candidates = await repository.GetLocationsWithinAsync(distance.BoundingBox(origin, radiusMiles), ct);
        return candidates
            .Select(l => (Location: l, Miles: distance.DistanceMiles(origin, l.Point)))
            .Where(x => x.Miles <= radiusMiles)
            .GroupBy(x => x.Location.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g =>
            {
                var nearest = g.MinBy(x => x.Miles);
                return new NearbyCompanyHit(g.Key, nearest.Location, Math.Round(nearest.Miles, 2), g.Any(x => x.Location.IsHeadquarters));
            }, StringComparer.OrdinalIgnoreCase);
    }
}
