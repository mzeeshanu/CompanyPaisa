using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.Analytics.Postgres;

/// <summary>
/// <c>Analytics:Provider = Postgres</c>. Setting: <c>ConnectionString</c>, a connection string or a postgresql:// URL
/// (Railway's DATABASE_URL). Keep it in environment variables. Without one, analytics are off and the site still runs.
/// </summary>
public sealed class PostgresAnalyticsAdapter : IAnalyticsAdapter
{
    public const string ConnectionStringKey = "ConnectionString";

    public string Name => "Postgres";

    public string? Problem(IConfigurationSection settings) =>
        string.IsNullOrWhiteSpace(settings[ConnectionStringKey])
            ? $"{settings.Path}:{ConnectionStringKey} is empty — set it to the Postgres database's URL."
            : null;

    public IAnalyticsDatabase Create(IServiceProvider services, IConfigurationSection settings) =>
        new PostgresAnalyticsStore(settings[ConnectionStringKey]!);
}

public static class PostgresAnalyticsAdapterExtensions
{
    /// <summary>Makes <c>Analytics:Provider = Postgres</c> available.</summary>
    public static AnalyticsAdapters AddPostgres(this AnalyticsAdapters adapters) => adapters.Add(new PostgresAnalyticsAdapter());
}
