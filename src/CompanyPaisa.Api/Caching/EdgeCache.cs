using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Caching;

/// <summary>
/// appsettings section "EdgeCache" — the cache in front of the site (Cloudflare) that keeps rendered pages. After each
/// start (a deploy, which is how new data arrives) the app empties it, so visitors and crawlers see the new data at once.
/// </summary>
public sealed class EdgeCacheOptions
{
    public const string SectionName = "EdgeCache";

    /// <summary>"None" (nothing to purge) or "Cloudflare". Empty means None.</summary>
    [RegularExpression("(?i)^ *(|None|Cloudflare) *$")] public string? Provider { get; set; } = "None";

    /// <summary>
    /// How long after starting to purge. The platform keeps sending visitors to the old copy until the new one passes its
    /// health check; purging before then would let the edge fill up again with the old pages.
    /// </summary>
    [Range(0, 3600)] public int PurgeDelaySeconds { get; set; } = 60;

    public CloudflareEdgeCacheOptions Cloudflare { get; set; } = new();
}

public sealed class CloudflareEdgeCacheOptions
{
    /// <summary>The zone's ID (Cloudflare dashboard → the domain → Overview → API, "Zone ID").</summary>
    public string? ZoneId { get; set; }

    /// <summary>An API token allowed only "Zone → Cache Purge → Purge" on this zone. A secret: set it on the host, never in a file.</summary>
    public string? ApiToken { get; set; }
}

/// <summary>A shared cache in front of the site that can be emptied. One implementation per provider, picked by EdgeCache:Provider.</summary>
public interface IEdgeCache
{
    string Name { get; }
    Task PurgeAllAsync(CancellationToken ct);
}

/// <summary>Cloudflare: "purge everything" on the zone (pages, and the hashed assets, which are simply fetched again).</summary>
public sealed class CloudflareEdgeCache(HttpClient http, CloudflareEdgeCacheOptions options) : IEdgeCache
{
    public const string ApiBase = "https://api.cloudflare.com/client/v4/";

    public string Name => "Cloudflare";

    public async Task PurgeAllAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}zones/{Uri.EscapeDataString(options.ZoneId!)}/purge_cache")
        {
            Content = JsonContent.Create(new { purge_everything = true })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var success = TryParse(body)?["success"]?.GetValue<bool>() == true;
        if (!response.IsSuccessStatusCode || !success)
            throw new HttpRequestException($"Cloudflare refused the purge ({(int)response.StatusCode}): {Errors(body)}");
    }

    private static JsonNode? TryParse(string body)
    {
        try { return JsonNode.Parse(body); } catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Cloudflare's own error messages (never the token, which isn't in the response).</summary>
    private static string Errors(string body) =>
        TryParse(body)?["errors"] is JsonArray errors && errors.Count > 0
            ? string.Join("; ", errors.Select(e => $"{e?["code"]} {e?["message"]}"))
            : body.Length > 200 ? body[..200] : body;
}

/// <summary>
/// Empties the edge cache once, <see cref="EdgeCacheOptions.PurgeDelaySeconds"/> after the app has started. Tries three
/// times; if it still fails the site carries on and the log says so (pages then refresh within Hosting:PageEdgeCacheSeconds).
/// </summary>
public sealed class EdgeCachePurgeOnStart(IEdgeCache cache, IOptions<EdgeCacheOptions> options, IHostApplicationLifetime lifetime,
    ILogger<EdgeCachePurgeOnStart> logger, IReadOnlyList<TimeSpan>? retries = null) : BackgroundService
{
    private readonly IReadOnlyList<TimeSpan> _retries = retries ?? [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    /// <summary>Whether the last run purged (for tests).</summary>
    public bool Purged { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                                        + "Check the EdgeCache settings, or purge from the Cloudflare dashboard.", cache.Name);
                    return;
                }
                logger.LogWarning(ex, "Purging the {Cache} cache failed; retrying", cache.Name);
                await Task.Delay(_retries[attempt], ct);
            }
        }
    }
}

public static class EdgeCacheExtensions
{
    /// <summary>
    /// Registers the purge named in EdgeCache:Provider. Cloudflare without a zone ID or token logs a warning at startup and
    /// purges nothing; the site runs either way.
    /// </summary>
    public static IServiceCollection AddEdgeCache(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EdgeCacheOptions.SectionName);
        services.AddOptions<EdgeCacheOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var o = section.Get<EdgeCacheOptions>() ?? new EdgeCacheOptions();
        if (!string.Equals(o.Provider?.Trim(), "Cloudflare", StringComparison.OrdinalIgnoreCase)) return services;

        if (string.IsNullOrWhiteSpace(o.Cloudflare.ZoneId) || string.IsNullOrWhiteSpace(o.Cloudflare.ApiToken))
        {
            services.AddHostedService(sp => new MissingSettingsWarning(sp.GetRequiredService<ILogger<EdgeCachePurgeOnStart>>()));
            return services;
        }
        services.AddHttpClient(nameof(CloudflareEdgeCache), c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IEdgeCache>(sp => new CloudflareEdgeCache(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CloudflareEdgeCache)), o.Cloudflare));
        services.AddHostedService(sp => new EdgeCachePurgeOnStart(sp.GetRequiredService<IEdgeCache>(),
            sp.GetRequiredService<IOptions<EdgeCacheOptions>>(), sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<EdgeCachePurgeOnStart>>()));
        return services;
    }

    private sealed class MissingSettingsWarning(ILogger logger) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            logger.LogWarning("EdgeCache:Provider is Cloudflare but EdgeCache:Cloudflare:ZoneId or ApiToken is empty; the cache won't be purged after a deploy");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
