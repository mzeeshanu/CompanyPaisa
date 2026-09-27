using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>The nearest named place to a point, from the same local tables (for labelling analytics; no runtime geocoding).</summary>
public interface IReverseGeoLocator
{
    /// <summary>The closest city or town centre within <paramref name="maxMiles"/>, or null.</summary>
    Task<GeoLookupResult?> NearestCityAsync(GeoPoint point, double maxMiles, CancellationToken ct = default);
}
