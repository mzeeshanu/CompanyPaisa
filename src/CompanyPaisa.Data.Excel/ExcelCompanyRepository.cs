using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Data.Excel;

/// <summary>
/// <see cref="ICompanyRepository"/> backed by an Excel workbook. The whole workbook is loaded into memory
/// (it's small) and swapped atomically when the file changes. A failed reload keeps the last good data.
/// </summary>
public sealed class ExcelCompanyRepository : ICompanyRepository, IDisposable
{
    private readonly ExcelDataSourceOptions _options;
    private readonly string _path;
    private readonly IClock _clock;
    private readonly IDataChangeSignal _changeSignal;
    private readonly ILogger<ExcelCompanyRepository> _logger;
    private readonly Lock _loadLock = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer? _debounce;
    private volatile DataSnapshot? _snapshot;

    public ExcelCompanyRepository(
        IOptions<ExcelDataSourceOptions> options,
        IFilePathResolver paths,
        IClock clock,
        IDataChangeSignal changeSignal,
        ILogger<ExcelCompanyRepository> logger)
    {
        _options = options.Value;
        _path = paths.Resolve(_options.Path);
        _clock = clock;
        _changeSignal = changeSignal;
        _logger = logger;

        var directory = Path.GetDirectoryName(_path);
        if (_options.ReloadOnChange && directory is not null && Directory.Exists(directory))
        {
            _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler onChange = (_, _) => _debounce.Change(_options.ReloadDebounceMs, Timeout.Infinite);
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, _) => _debounce.Change(_options.ReloadDebounceMs, Timeout.Infinite);
        }
    }

    private DataSnapshot Data
    {
        get
        {
            if (_snapshot is { } s) return s;
            lock (_loadLock)
            {
                return _snapshot ??= Load();
            }
        }
    }

    private DataSnapshot Load()
    {
        var snapshot = ExcelWorkbookReader.Read(_path, _options.Sheets, _clock.UtcNow);
        _logger.LogInformation("Loaded {Companies} companies, {Locations} locations from {Path} (version {Version}{Sample})",
            snapshot.Companies.Count, snapshot.Locations.Count, _path, snapshot.Metadata.DataVersion,
            snapshot.Metadata.IsSampleData ? ", SAMPLE DATA" : "");
        return snapshot;
    }

    private void Reload()
    {
        try
        {
            var fresh = Load();
            lock (_loadLock) _snapshot = fresh;
            _changeSignal.Signal();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reloading {Path} failed; keeping the previously loaded data", _path);
        }
    }

    public Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default) => Task.FromResult(Data.Companies);

    public Task<Company?> GetCompanyAsync(string companyIdOrTicker, CancellationToken ct = default) =>
        Task.FromResult(Data.Find(companyIdOrTicker.Trim()));

    public Task<IReadOnlyList<Company>> GetCompaniesAsync(IEnumerable<string> companyIds, CancellationToken ct = default)
    {
        var data = Data;
        IReadOnlyList<Company> list = companyIds.Select(id => data.CompaniesById.GetValueOrDefault(id)).OfType<Company>().ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CompanyLocation>> GetLocationsWithinAsync(GeoBoundingBox box, CancellationToken ct = default)
    {
        IReadOnlyList<CompanyLocation> list = Data.Locations.Where(l => box.Contains(l.Point)).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CompanyLocation>> GetLocationsAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult(Data.LocationsByCompany.GetValueOrDefault(companyId) ?? []);

    public Task<IReadOnlyList<FinancialPeriod>> GetFinancialsAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult(Data.FinancialsByCompany.GetValueOrDefault(companyId) ?? []);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>> GetFinancialsAsync(IEnumerable<string> companyIds, CancellationToken ct = default)
    {
        var data = Data;
        IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>> result = companyIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, id => data.FinancialsByCompany.GetValueOrDefault(id) ?? [], StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult(Data.ExecutivesByCompany.GetValueOrDefault(companyId) ?? []);

    public Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default) => Task.FromResult(Data.Sectors);

    public Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default) => Task.FromResult(Data.Metadata);

    public Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default) =>
        Task.FromResult((Data.Companies.Count, Data.Locations.Count));

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
