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
