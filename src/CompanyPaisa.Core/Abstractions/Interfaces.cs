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
    Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(IEnumerable<string> companyIds, CancellationToken ct = default);

    /// <summary>Every pay row for these people, at any company.</summary>
    Task<IReadOnlyList<ExecutiveCompensation>> GetCompensationForPeopleAsync(IEnumerable<string> personIds, CancellationToken ct = default);
    Task<Person?> GetPersonAsync(string personId, CancellationToken ct = default);

    /// <summary>Officer appointments these companies announced, with their stated packages (none if the source has no such data).</summary>
    Task<IReadOnlyList<NewExecutive>> GetNewExecutivesAsync(IEnumerable<string> companyIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NewExecutive>>([]);

    /// <summary>The company's disclosed median-employee pay and CEO pay ratio, by year (none if the source has no such data).</summary>
    Task<IReadOnlyList<WorkerPay>> GetWorkerPayAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WorkerPay>>([]);

    /// <summary>Salaries by job title from the company's H-1B wage filings (company-wide rows and per-place rows).</summary>
    Task<IReadOnlyList<JobSalary>> GetJobSalariesAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobSalary>>([]);

    /// <summary>Where each kind of job salaries comes from (visa filings, job ads); empty when there are none.</summary>
    Task<IReadOnlyList<JobSalarySource>> GetJobSalarySourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<JobSalarySource>>([]);

    Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default);
    Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default);
    Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default);
}

/// <summary>Resolves a ZIP code or "City, ST" to coordinates from local data (no runtime geocoding).</summary>
public interface IGeoLocator
{
    Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default);
}

/// <summary>The nearest named place to a point, from the same local tables (for labelling analytics; no runtime geocoding).</summary>
public interface IReverseGeoLocator
{
    /// <summary>The closest city or town centre within <paramref name="maxMiles"/>, or null.</summary>
    Task<GeoLookupResult?> NearestCityAsync(GeoPoint point, double maxMiles, CancellationToken ct = default);
}

/// <summary>Distance maths. Default is straight-line (haversine).</summary>
public interface IDistanceCalculator
{
    double DistanceMiles(GeoPoint from, GeoPoint to);
    GeoBoundingBox BoundingBox(GeoPoint center, double radiusMiles);
}

/// <summary>A company with a location inside the search radius, and its closest such location.</summary>
public sealed record NearbyCompanyHit(string CompanyId, CompanyLocation NearestLocation, double DistanceMiles, bool HasHeadquartersInRange);

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
