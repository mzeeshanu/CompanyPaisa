using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Resolves a ZIP code or "City, ST" to coordinates from local data (no runtime geocoding).</summary>
public interface IGeoLocator
{
    Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default);
}
