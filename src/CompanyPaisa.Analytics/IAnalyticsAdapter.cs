using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.Analytics;

/// <summary>A store that both keeps events and answers the dashboard: what an adapter hands out.</summary>
public interface IAnalyticsDatabase : IAnalyticsStore, IAnalyticsReader;

/// <summary>
/// One way of keeping the site's analytics (a SQLite file, a Postgres database, a hosted service…). Each adapter lives in its
/// own project with its own dependencies and is registered by name (<see cref="AnalyticsAdapters"/>); <c>Analytics:Provider</c>
/// picks one. Switching provider is a setting, not a code change.
/// </summary>
public interface IAnalyticsAdapter
{
    /// <summary>The value of <c>Analytics:Provider</c> that selects this adapter (matched ignoring case).</summary>
    string Name { get; }

    /// <summary>
    /// Null when <paramref name="settings"/> are enough to record; otherwise why not, for the dashboard and the log.
    /// Analytics are then off and the site keeps running.
    /// </summary>
    string? Problem(IConfigurationSection settings);

    /// <summary>The store, from its settings (the adapter reads its own keys from <paramref name="settings"/>).</summary>
    IAnalyticsDatabase Create(IServiceProvider services, IConfigurationSection settings);
}
