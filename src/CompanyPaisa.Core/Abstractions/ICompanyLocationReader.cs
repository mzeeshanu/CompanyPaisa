using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Where companies have offices and sites.</summary>
public interface ICompanyLocationReader
{
    /// <summary>Locations inside a lat/long rectangle (a spatial index in SQL, a scan in memory).</summary>
    Task<IReadOnlyList<CompanyLocation>> GetLocationsWithinAsync(GeoBoundingBox box, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyLocation>> GetLocationsAsync(string companyId, CancellationToken ct = default);
}
