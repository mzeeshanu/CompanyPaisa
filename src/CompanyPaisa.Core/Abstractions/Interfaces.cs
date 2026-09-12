using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>
/// The only way the application reads company data. Implementations: Excel (v1), SQL (later).
/// Nothing above this interface knows where the data lives.
/// </summary>
public interface ICompanyRepository
{
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default);
    Task<Company?> GetCompanyAsync(string companyIdOrTicker, CancellationToken ct = default);
    Task<IReadOnlyList<Company>> GetCompaniesAsync(IEnumerable<string> companyIds, CancellationToken ct = default);

    /// <summary>Locations inside a lat/long rectangle (a spatial index in SQL, a scan in memory).</summary>
    Task<IReadOnlyList<CompanyLocation>> GetLocationsWithinAsync(GeoBoundingBox box, CancellationToken ct = default);
    Task<IReadOnlyList<CompanyLocation>> GetLocationsAsync(string companyId, CancellationToken ct = default);

    Task<IReadOnlyList<FinancialPeriod>> GetFinancialsAsync(string companyId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>> GetFinancialsAsync(IEnumerable<string> companyIds, CancellationToken ct = default);

    Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(string companyId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default);
    Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default);
    Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default);
}

/// <summary>Resolves a ZIP code or "City, ST" to coordinates from local data (no runtime geocoding).</summary>
public interface IGeoLocator
{
    Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default);
}

/// <summary>Distance maths. Default is straight-line (haversine).</summary>
public interface IDistanceCalculator
{
    double DistanceMiles(GeoPoint from, GeoPoint to);
    GeoBoundingBox BoundingBox(GeoPoint center, double radiusMiles);
}

/// <summary>Computes TTM, growth, CAGR, margin and trend status.</summary>
public interface IFinancialMetricsService
{
    CompanyIndicators Compute(IReadOnlyList<FinancialPeriod> periods);
    /// <summary>Adds year-over-year revenue growth to each period of one type.</summary>
    IReadOnlyList<(FinancialPeriod Period, decimal? RevenueGrowthYoY)> WithGrowth(IReadOnlyList<FinancialPeriod> periods, PeriodType type);
}

/// <summary>Current time, abstracted so date logic can be tested.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Turns relative paths from configuration into absolute paths (relative to the app's content root).</summary>
public interface IFilePathResolver
{
    string Resolve(string path);
}

/// <summary>
/// Raised by a data source when its data changes (e.g. the workbook was replaced).
/// Caches include <see cref="Generation"/> in their keys, so a change invalidates everything at once.
/// </summary>
public interface IDataChangeSignal
{
    long Generation { get; }
    void Signal();
}
