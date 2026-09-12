using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Tests;

/// <summary>In-memory repository for handler tests.</summary>
internal sealed class FakeRepository : ICompanyRepository
{
    public List<Company> Companies { get; } = [];
    public List<CompanyLocation> Locations { get; } = [];
    public List<FinancialPeriod> Financials { get; } = [];
    public List<ExecutiveCompensation> Executives { get; } = [];

    public FakeRepository Add(string ticker, string sector, LocationType type, double lat, double lng, decimal annualRevenue, decimal growth = 0.05m, decimal margin = 0.1m)
    {
        Companies.Add(new Company { CompanyId = ticker, Name = ticker + " Inc", Ticker = ticker, Exchange = "NASDAQ", Sector = sector });
        Locations.Add(new CompanyLocation { LocationId = $"{ticker}-{Locations.Count}", CompanyId = ticker, Type = type, Label = type.ToString(), City = "Town", State = "UT", Point = new GeoPoint(lat, lng) });
        // 8 quarters: prior year at base, latest year grown by `growth`.
        for (var i = 0; i < 8; i++)
        {
            var rev = annualRevenue / 4 * (i < 4 ? 1 : 1 + growth);
            Financials.Add(new FinancialPeriod { CompanyId = ticker, PeriodType = PeriodType.Quarterly, FiscalYear = 2024 + i / 4, FiscalQuarter = i % 4 + 1, Revenue = rev, NetIncome = rev * margin });
        }
        return this;
    }

    public FakeRepository AddLocation(string ticker, LocationType type, double lat, double lng)
    {
        Locations.Add(new CompanyLocation { LocationId = $"{ticker}-{Locations.Count}", CompanyId = ticker, Type = type, Label = type.ToString(), City = "Town", State = "UT", Point = new GeoPoint(lat, lng) });
        return this;
    }

    public Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Company>>(Companies);
    public Task<Company?> GetCompanyAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(Companies.FirstOrDefault(c => c.CompanyId.Equals(id, StringComparison.OrdinalIgnoreCase)));
    public Task<IReadOnlyList<Company>> GetCompaniesAsync(IEnumerable<string> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Company>>(Companies.Where(c => ids.Contains(c.CompanyId)).ToList());
    public Task<IReadOnlyList<CompanyLocation>> GetLocationsWithinAsync(GeoBoundingBox box, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CompanyLocation>>(Locations.Where(l => box.Contains(l.Point)).ToList());
    public Task<IReadOnlyList<CompanyLocation>> GetLocationsAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CompanyLocation>>(Locations.Where(l => l.CompanyId == id).ToList());
    public Task<IReadOnlyList<FinancialPeriod>> GetFinancialsAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FinancialPeriod>>(Financials.Where(f => f.CompanyId == id).ToList());
    public Task<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>> GetFinancialsAsync(IEnumerable<string> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>>(
            ids.ToDictionary(id => id, id => (IReadOnlyList<FinancialPeriod>)Financials.Where(f => f.CompanyId == id).ToList()));
    public Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ExecutiveCompensation>>(Executives.Where(e => e.CompanyId == id).ToList());
    public Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Companies.Select(c => c.Sector).Distinct().ToList());
    public Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default) =>
        Task.FromResult(new DataSetMetadata("test", null, true, DateTimeOffset.UnixEpoch));
    public Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default) => Task.FromResult((Companies.Count, Locations.Count));
}

internal sealed class FakeGeoLocator(params (string Query, GeoPoint Point)[] entries) : IGeoLocator
{
    public Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default) =>
        Task.FromResult(entries.Where(e => e.Query == query).Select(e => new GeoLookupResult(query, "Lehi", "UT", query, e.Point)).FirstOrDefault());
}

internal static class Opt
{
    public static IOptionsMonitor<T> Monitor<T>(T value) where T : class => new StaticMonitor<T>(value);

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
