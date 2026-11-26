using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.Analytics;

/// <summary>
/// The analytics adapters a host ships, by name. The host adds the ones it references
/// (<c>adapters =&gt; adapters.AddSqlite().AddPostgres()</c>); "None" is always there and records nothing.
/// </summary>
public sealed class AnalyticsAdapters
{
    public const string None = "None";

    private readonly Dictionary<string, IAnalyticsAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

    public AnalyticsAdapters Add(IAnalyticsAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.Name.Equals(None, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"\"{None}\" is reserved for recording nothing.", nameof(adapter));
        _adapters[adapter.Name] = adapter;
        return this;
    }

    /// <summary>Every name <c>Analytics:Provider</c> accepts, "None" first.</summary>
    public IReadOnlyList<string> Names => [None, .. _adapters.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The adapter called <paramref name="name"/>; null for "None" (or empty). Unknown names throw, listing the known ones.</summary>
    public IAnalyticsAdapter? Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals(None, StringComparison.OrdinalIgnoreCase)) return null;
        return _adapters.TryGetValue(name.Trim(), out var adapter)
            ? adapter
            : throw new InvalidOperationException(
                $"Analytics:Provider '{name}' isn't supported by this build. Supported: {string.Join(", ", Names)}.");
    }

    /// <summary>
    /// Whether <paramref name="name"/> can record with <paramref name="settings"/>, and why not. Unknown names throw, so a typo
    /// stops the app at startup instead of silently recording nothing.
    /// </summary>
    public AnalyticsStatus Status(string? name, IConfigurationSection settings)
    {
        var adapter = Get(name);
        if (adapter is null) return new AnalyticsStatus(None, false, "Analytics:Provider is None.");
        var problem = adapter.Problem(settings);
        return problem is null
            ? new AnalyticsStatus(adapter.Name, true, $"Recording to {adapter.Name}.")
            : new AnalyticsStatus(adapter.Name, false, problem);
    }
}
