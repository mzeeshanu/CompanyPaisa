using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Data;

/// <summary>
/// <see cref="ICompanyRepository"/> over an in-memory <see cref="DataSnapshot"/>. A data source only says how to read
/// its files (<see cref="Read"/>); this class checks the rows (<see cref="DataRules"/>), loads lazily, watches the files
/// and swaps in a fresh snapshot when they change. A failed reload keeps the last good data.
/// </summary>
public abstract class SnapshotRepository : ICompanyRepository, IDisposable
{
    private readonly IClock _clock;
    private readonly IDataChangeSignal _changeSignal;
    private readonly ILogger _logger;
    private readonly Lock _loadLock = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer? _debounce;
    private volatile DataSnapshot? _snapshot;

    /// <param name="watchedFiles">Files whose change triggers a reload (full paths).</param>
    protected SnapshotRepository(IReadOnlyList<string> watchedFiles, bool reloadOnChange, int debounceMs,
        IClock clock, IDataChangeSignal changeSignal, ILogger logger)
    {
        _clock = clock;
        _changeSignal = changeSignal;
        _logger = logger;
        if (!reloadOnChange) return;

        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        var watched = watchedFiles.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in watched.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            void Schedule(string fullPath) { if (watched.Contains(fullPath)) _debounce.Change(debounceMs, Timeout.Infinite); }
            watcher.Changed += (_, e) => Schedule(e.FullPath);
            watcher.Created += (_, e) => Schedule(e.FullPath);
            watcher.Renamed += (_, e) => Schedule(e.FullPath);
            _watchers.Add(watcher);
        }
    }

    /// <summary>Reads the rows from storage (no checks needed; <see cref="DataRules"/> runs afterwards).</summary>
    protected abstract CompanyData Read(DateTimeOffset loadedAt);

    /// <summary>Where the data comes from, for log and error messages.</summary>
    protected abstract string Source { get; }

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
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = new DataSnapshot(DataRules.Check(Read(_clock.UtcNow), Source));
        _logger.LogInformation("Loaded {Companies} companies, {Locations} locations from {Source} in {Ms} ms (version {Version}{Sample})",
            snapshot.Companies.Count, snapshot.Locations.Count, Source, watch.ElapsedMilliseconds, snapshot.Metadata.DataVersion,
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
            _logger.LogError(ex, "Reloading {Source} failed; keeping the previously loaded data", Source);
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

    public Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(IEnumerable<string> companyIds, CancellationToken ct = default)
    {
        var data = Data;
        IReadOnlyList<ExecutiveCompensation> list = companyIds.Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(id => data.ExecutivesByCompany.GetValueOrDefault(id) ?? []).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ExecutiveCompensation>> GetCompensationForPeopleAsync(IEnumerable<string> personIds, CancellationToken ct = default)
    {
        var data = Data;
        IReadOnlyList<ExecutiveCompensation> list = personIds.Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(id => data.CompensationByPerson.GetValueOrDefault(id) ?? []).ToList();
        return Task.FromResult(list);
    }

    public Task<Person?> GetPersonAsync(string personId, CancellationToken ct = default) =>
        Task.FromResult(Data.PeopleById.GetValueOrDefault(personId.Trim()));

    public Task<IReadOnlyList<NewExecutive>> GetNewExecutivesAsync(IEnumerable<string> companyIds, CancellationToken ct = default)
    {
        var data = Data;
        IReadOnlyList<NewExecutive> list = companyIds.Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(id => data.NewExecutivesByCompany.GetValueOrDefault(id) ?? []).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<WorkerPay>> GetWorkerPayAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult(Data.WorkerPayByCompany.GetValueOrDefault(companyId) ?? []);

    public Task<IReadOnlyList<JobSalary>> GetJobSalariesAsync(string companyId, CancellationToken ct = default) =>
        Task.FromResult(Data.JobSalariesByCompany.GetValueOrDefault(companyId) ?? []);

    public Task<JobSalarySource?> GetJobSalarySourceAsync(CancellationToken ct = default) => Task.FromResult(Data.SalarySource);

    public Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default) => Task.FromResult(Data.Sectors);

    public Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default) => Task.FromResult(Data.Metadata);

    public Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default) =>
        Task.FromResult((Data.Companies.Count, Data.Locations.Count));

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _debounce?.Dispose();
        GC.SuppressFinalize(this);
    }
}
