using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Analytics;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the adapter named in Analytics:Provider (from the ones <paramref name="adapters"/> adds), the queue and the
    /// background writer. An adapter whose settings are incomplete (Postgres without a connection string) turns analytics off
    /// instead of stopping the site; an unknown provider name stops it at startup. The returned status says which.
    /// The host still decides what to record (the website registers an IAnalyticsTracker when this is enabled).
    /// </summary>
    public static AnalyticsStatus AddCompanyPaisaAnalytics(this IServiceCollection services, IConfiguration configuration,
        Action<AnalyticsAdapters> adapters)
    {
        var section = configuration.GetSection(AnalyticsOptions.SectionName);
        services.AddOptions<AnalyticsOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var o = section.Get<AnalyticsOptions>() ?? new AnalyticsOptions();

        var registry = new AnalyticsAdapters();
        adapters(registry);
        services.AddSingleton(registry);

        var status = registry.Status(o.Provider, section);
        services.AddSingleton(status);
        if (!status.Enabled) return status;

        var adapter = registry.Get(o.Provider)!;
        services.AddSingleton<IAnalyticsDatabase>(sp => adapter.Create(sp, section));
        services.AddSingleton<IAnalyticsStore>(sp => sp.GetRequiredService<IAnalyticsDatabase>());
        services.AddSingleton<IAnalyticsReader>(sp => sp.GetRequiredService<IAnalyticsDatabase>());

        // Analytics:ImportFrom — copy the history from another provider once (an unknown name stops startup, like Provider).
        var importSection = section.GetSection(AnalyticsImport.SectionName);
        var importFrom = registry.Get(importSection[nameof(AnalyticsOptions.Provider)]);
        var importSource = importFrom is null ? null : new AnalyticsImportSource(importFrom, importSection);
        services.AddSingleton(sp => new AnalyticsImport(sp, sp.GetRequiredService<ILogger<AnalyticsImport>>(), importSource));

        services.AddSingleton<AnalyticsQueue>();
        services.AddSingleton<IAnalyticsSink>(sp => sp.GetRequiredService<AnalyticsQueue>());
        services.AddHostedService<AnalyticsWriter>();
        return status;
    }
}
