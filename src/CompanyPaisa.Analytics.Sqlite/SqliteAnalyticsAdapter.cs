using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyPaisa.Analytics.Sqlite;

/// <summary>
/// <c>Analytics:Provider = Sqlite</c>. Setting: <c>SqlitePath</c>, the file, relative to the app's content root (keep it out of
/// data/*.db, which ships with the app; in production, a path on a mounted volume).
/// </summary>
public sealed class SqliteAnalyticsAdapter : IAnalyticsAdapter
{
    public const string PathKey = "SqlitePath";
    public const string DefaultPath = "../../data/analytics/analytics.db";

    public string Name => "Sqlite";

    public string? Problem(IConfigurationSection settings) => null;

    public IAnalyticsDatabase Create(IServiceProvider services, IConfigurationSection settings)
    {
        var path = settings[PathKey] is { Length: > 0 } p ? p : DefaultPath;
        return new SqliteAnalyticsStore(services.GetRequiredService<IFilePathResolver>().Resolve(path));
    }
}

public static class SqliteAnalyticsAdapterExtensions
{
    /// <summary>Makes <c>Analytics:Provider = Sqlite</c> available.</summary>
    public static AnalyticsAdapters AddSqlite(this AnalyticsAdapters adapters) => adapters.Add(new SqliteAnalyticsAdapter());
}
