using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CompanyPaisa.EdgeCache.Cloudflare;

/// <summary>Settings section "EdgeCache:Cloudflare".</summary>
public sealed class CloudflareEdgeCacheOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>The zone's ID (Cloudflare dashboard → the domain → Overview → API, "Zone ID").</summary>
    public string? ZoneId { get; set; }

    /// <summary>An API token allowed only "Zone → Cache Purge → Purge" on this zone. A secret: set it on the host, never in a file.</summary>
    public string? ApiToken { get; set; }
}

/// <summary>
/// <c>EdgeCache:Provider = Cloudflare</c>. Settings: <c>EdgeCache:Cloudflare:ZoneId</c> and <c>EdgeCache:Cloudflare:ApiToken</c>;
/// without both, the site runs and nothing is purged.
/// </summary>
public sealed class CloudflareEdgeCacheAdapter : IEdgeCacheAdapter
{
    public string Name => "Cloudflare";

    private static CloudflareEdgeCacheOptions Read(IConfigurationSection settings) =>
        settings.GetSection(CloudflareEdgeCacheOptions.SectionName).Get<CloudflareEdgeCacheOptions>() ?? new();

    public string? Problem(IConfigurationSection settings)
    {
        var o = Read(settings);
        return string.IsNullOrWhiteSpace(o.ZoneId) || string.IsNullOrWhiteSpace(o.ApiToken)
            ? "EdgeCache:Cloudflare:ZoneId or EdgeCache:Cloudflare:ApiToken is empty."
            : null;
    }

    /// <summary>
    /// The client lives as long as the app (one purge per start), so it is made here rather than through IHttpClientFactory;
    /// connections are still renewed every few minutes so a DNS change is picked up.
    /// </summary>
    public IEdgeCache Create(IServiceProvider services, IConfigurationSection settings) => new CloudflareEdgeCache(
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }) { Timeout = TimeSpan.FromSeconds(30) },
        Read(settings));
}

public static class CloudflareEdgeCacheAdapterExtensions
{
    /// <summary>Makes <c>EdgeCache:Provider = Cloudflare</c> available.</summary>
    public static EdgeCacheAdapters AddCloudflare(this EdgeCacheAdapters adapters) => adapters.Add(new CloudflareEdgeCacheAdapter());
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
