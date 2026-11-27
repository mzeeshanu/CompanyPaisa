using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.EdgeCache;

/// <summary>Whether the edge cache is purged after a deploy, by which provider, and why not.</summary>
public sealed record EdgeCacheStatus(string Provider, bool Enabled, string Reason);

/// <summary>
/// The edge cache adapters a host ships, by name. The host adds the ones it references
/// (<c>adapters =&gt; adapters.AddCloudflare()</c>); "None" is always there and purges nothing.
/// </summary>
public sealed class EdgeCacheAdapters
{
    public const string None = "None";

    private readonly Dictionary<string, IEdgeCacheAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

    public EdgeCacheAdapters Add(IEdgeCacheAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapter.Name.Equals(None, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"\"{None}\" is reserved for purging nothing.", nameof(adapter));
        _adapters[adapter.Name] = adapter;
        return this;
    }

    /// <summary>Every name <c>EdgeCache:Provider</c> accepts, "None" first.</summary>
    public IReadOnlyList<string> Names => [None, .. _adapters.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The adapter called <paramref name="name"/>; null for "None" (or empty). Unknown names throw, listing the known ones.</summary>
    public IEdgeCacheAdapter? Get(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Equals(None, StringComparison.OrdinalIgnoreCase)) return null;
        return _adapters.TryGetValue(name, out var adapter)
            ? adapter
            : throw new InvalidOperationException(
                $"EdgeCache:Provider '{name}' isn't supported by this build. Supported: {string.Join(", ", Names)}.");
    }

    /// <summary>
    /// Whether <paramref name="name"/> can purge with <paramref name="settings"/>, and why not. Unknown names throw, so a typo
    /// stops the app at startup instead of silently never purging.
    /// </summary>
    public EdgeCacheStatus Status(string? name, IConfigurationSection settings)
    {
        var adapter = Get(name);
        if (adapter is null) return new EdgeCacheStatus(None, false, "EdgeCache:Provider is None.");
        var problem = adapter.Problem(settings);
        return problem is null
            ? new EdgeCacheStatus(adapter.Name, true, $"Purging {adapter.Name} after each start.")
            : new EdgeCacheStatus(adapter.Name, false, problem);
    }
}
