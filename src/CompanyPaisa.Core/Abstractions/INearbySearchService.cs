using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>A company with a location inside the search radius, and its closest such location.</summary>
public sealed record NearbyCompanyHit(string CompanyId, CompanyLocation NearestLocation, double DistanceMiles, bool HasHeadquartersInRange);

/// <summary>
/// Where a search looked and what it found: a radius around <see cref="Origin"/>, or (with <see cref="Region"/>) a whole country
/// or state, whose <see cref="Origin"/> is the middle of its companies and whose distances are all 0.
/// </summary>
public sealed record SearchArea(GeoPoint Origin, string? Label, double RadiusMiles, Services.Region? Region, IReadOnlyDictionary<string, NearbyCompanyHit> Hits);

/// <summary>
/// Shared by every "near me" search (companies, executives…): resolves where the search starts
/// and which companies have a location within the radius.
/// </summary>
public interface INearbySearchService
{
    /// <summary>Coordinates win; otherwise the ZIP / "City, ST" in <paramref name="near"/> is looked up. Throws NotFound if unknown.</summary>
    Task<(GeoPoint Point, string? Label)> ResolveOriginAsync(string? near, double? latitude, double? longitude, CancellationToken ct = default);

    /// <summary>Companies with at least one location within the radius, keyed by company id.</summary>
    Task<IReadOnlyDictionary<string, NearbyCompanyHit>> FindCompaniesAsync(GeoPoint origin, double radiusMiles, CancellationToken ct = default);

    /// <summary>Companies with at least one location in the country or state (their headquarters there when they have one).</summary>
    Task<IReadOnlyDictionary<string, NearbyCompanyHit>> FindCompaniesInRegionAsync(Services.Region region, CancellationToken ct = default);

    /// <summary>
    /// The whole search area: <paramref name="region"/> (a code or name) or a <paramref name="near"/> that names a region searches
    /// that country or state; otherwise the radius around the coordinates or the ZIP / city in <paramref name="near"/>.
    /// </summary>
    Task<SearchArea> FindAreaAsync(string? near, string? region, double? latitude, double? longitude, double radiusMiles, CancellationToken ct = default);
}
