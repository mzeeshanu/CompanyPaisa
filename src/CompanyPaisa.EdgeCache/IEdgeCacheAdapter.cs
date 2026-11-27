using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.EdgeCache;

/// <summary>A shared cache in front of the site (a CDN) that can be emptied.</summary>
public interface IEdgeCache
{
    string Name { get; }
    Task PurgeAllAsync(CancellationToken ct);
}

/// <summary>
/// One edge cache provider (Cloudflare, Fastly, Bunny…). Each adapter lives in its own project with its own dependencies and is
/// registered by name (<see cref="EdgeCacheAdapters"/>); <c>EdgeCache:Provider</c> picks one. Switching provider is a setting,
/// not a code change.
/// </summary>
public interface IEdgeCacheAdapter
{
    /// <summary>The value of <c>EdgeCache:Provider</c> that selects this adapter (matched ignoring case).</summary>
    string Name { get; }

    /// <summary>
    /// Null when <paramref name="settings"/> are enough to purge; otherwise why not, for the log. The site then runs without
    /// purging (pages refresh when Cloudflare's copy expires).
    /// </summary>
    string? Problem(IConfigurationSection settings);

    /// <summary>The cache, from its settings (the adapter reads its own keys from <paramref name="settings"/>, the EdgeCache section).</summary>
    IEdgeCache Create(IServiceProvider services, IConfigurationSection settings);
}
