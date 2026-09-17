using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Analytics;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the store named in Analytics:Provider ("None", "Sqlite", "Postgres"), the queue and the background writer.
    /// Postgres without a connection string turns analytics off instead of stopping the site. The returned status says which.
    /// The host still decides what to record (the website registers an <see cref="IAnalyticsTracker"/> when this is enabled).
    /// </summary>
    public static AnalyticsStatus AddCompanyPaisaAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AnalyticsOptions.SectionName);
        services.AddOptions<AnalyticsOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var o = section.Get<AnalyticsOptions>() ?? new AnalyticsOptions();

        var status = o.Provider.ToLowerInvariant() switch
        {
            "sqlite" => new AnalyticsStatus("Sqlite", true, "Recording to a local SQLite file."),
            "postgres" when string.IsNullOrWhiteSpace(o.ConnectionString) =>
                new AnalyticsStatus("Postgres", false, "Analytics:ConnectionString is empty — set it to the Postgres database's URL."),
            "postgres" => new AnalyticsStatus("Postgres", true, "Recording to Postgres."),
            _ => new AnalyticsStatus("None", false, "Analytics:Provider is None.")
        };
        services.AddSingleton(status);
        if (!status.Enabled) return status;

        if (status.Provider == "Sqlite")
            services.AddSingleton<DbAnalyticsStore>(sp => new SqliteAnalyticsStore(sp.GetRequiredService<IFilePathResolver>().Resolve(o.SqlitePath)));
        else
            services.AddSingleton<DbAnalyticsStore>(_ => new PostgresAnalyticsStore(o.ConnectionString!));
        services.AddSingleton<IAnalyticsStore>(sp => sp.GetRequiredService<DbAnalyticsStore>());
        services.AddSingleton<IAnalyticsReader>(sp => sp.GetRequiredService<DbAnalyticsStore>());

        services.AddSingleton<AnalyticsQueue>();
        services.AddSingleton<IAnalyticsSink>(sp => sp.GetRequiredService<AnalyticsQueue>());
        services.AddHostedService<AnalyticsWriter>();
        return status;
    }
}
