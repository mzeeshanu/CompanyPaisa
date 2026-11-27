using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.EdgeCache;

/// <summary>
/// appsettings section "EdgeCache" — the cache in front of the site that keeps rendered pages. After each start (a deploy,
/// which is how new data arrives) the app empties it, so visitors and crawlers see the new data at once. Each adapter reads
/// its own keys from this section (Cloudflare: EdgeCache:Cloudflare:ZoneId and ApiToken).
/// </summary>
public sealed class EdgeCacheOptions
{
    public const string SectionName = "EdgeCache";

    /// <summary>"None" (nothing to purge) or an adapter's name, such as "Cloudflare". Empty means None.</summary>
    public string? Provider { get; set; } = EdgeCacheAdapters.None;

    /// <summary>
    /// How long after starting to purge. The platform keeps sending visitors to the old copy until the new one passes its
    /// health check; purging before then would let the edge fill up again with the old pages.
    /// </summary>
    [Range(0, 3600)] public int PurgeDelaySeconds { get; set; } = 60;
}

/// <summary>
/// Empties the edge cache once, <see cref="EdgeCacheOptions.PurgeDelaySeconds"/> after the app has started. Tries three
/// times; if it still fails the site carries on and the log says so (pages then refresh when the edge's copy expires).
/// Registered whenever a provider is named: without a usable cache (settings missing) it only logs why it won't purge.
/// </summary>
public sealed class EdgeCachePurgeOnStart(IEdgeCache? cache, EdgeCacheStatus status, IOptions<EdgeCacheOptions> options,
    IHostApplicationLifetime lifetime, ILogger<EdgeCachePurgeOnStart> logger, IReadOnlyList<TimeSpan>? retries = null) : BackgroundService
{
    private readonly IReadOnlyList<TimeSpan> _retries = retries ?? [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>Whether the last run purged (for tests).</summary>
    public bool Purged { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (cache is null)
        {
            logger.LogWarning("The {Provider} edge cache won't be purged after a deploy: {Reason}", status.Provider, status.Reason);
            return;
        }
        var started = new TaskCompletionSource();
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        using (stoppingToken.Register(() => started.TrySetCanceled()))
        {
            try { await started.Task; } catch (TaskCanceledException) { return; }
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.PurgeDelaySeconds), stoppingToken);
            await PurgeAsync(stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    public async Task PurgeAsync(CancellationToken ct)
    {
        if (cache is null) return;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await cache.PurgeAllAsync(ct);
                Purged = true;
                logger.LogInformation("Purged the {Cache} cache, so pages show this deploy's data", cache.Name);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= _retries.Count)
                {
                    logger.LogError(ex, "Couldn't purge the {Cache} cache; pages may show the previous data until they expire. "
                                        + "Check the EdgeCache settings, or purge from the provider's dashboard.", cache.Name);
                    return;
                }
                logger.LogWarning(ex, "Purging the {Cache} cache failed; retrying", cache.Name);
                await Task.Delay(_retries[attempt], ct);
            }
        }
    }
}
