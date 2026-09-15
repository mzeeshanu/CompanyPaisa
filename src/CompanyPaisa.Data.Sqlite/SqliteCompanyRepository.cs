using System.ComponentModel.DataAnnotations;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Data.Sqlite;

/// <summary>appsettings section "DataSource:Sqlite".</summary>
public sealed class SqliteDataSourceOptions
{
    public const string SectionName = "DataSource:Sqlite";

    /// <summary>Path to the database file. Relative paths resolve from the app's content root.</summary>
    [Required] public string Path { get; set; } = "";

    /// <summary>Reload automatically when the importer replaces the file.</summary>
    public bool ReloadOnChange { get; set; } = true;

    /// <summary>Wait this long after the last change before reloading.</summary>
    [Range(0, 60_000)] public int ReloadDebounceMs { get; set; } = 1500;
}

/// <summary>The data set from the SQLite file the importers publish (every market in one file).</summary>
public sealed class SqliteCompanyRepository(
    IOptions<SqliteDataSourceOptions> options,
    IFilePathResolver paths,
    IClock clock,
    IDataChangeSignal changeSignal,
    ILogger<SqliteCompanyRepository> logger)
    : SnapshotRepository([paths.Resolve(options.Value.Path)], options.Value.ReloadOnChange, options.Value.ReloadDebounceMs, clock, changeSignal, logger)
{
    private readonly string _path = paths.Resolve(options.Value.Path);

    protected override string Source => _path;

    protected override CompanyData Read(DateTimeOffset loadedAt) => SqliteDataStore.Read(_path, loadedAt);
}

public static class DependencyInjection
{
    /// <summary>Registers the SQLite database as the <see cref="ICompanyRepository"/>.</summary>
    public static IServiceCollection AddSqliteDataSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<SqliteDataSourceOptions>(configuration, SqliteDataSourceOptions.SectionName);
        services.AddSingleton<ICompanyRepository, SqliteCompanyRepository>();
        return services;
    }
}
