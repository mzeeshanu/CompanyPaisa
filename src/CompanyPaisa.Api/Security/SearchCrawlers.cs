using System.Net;
using CompanyPaisa.Api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Security;

/// <summary>
/// Search engines' crawlers, which the hourly caps leave alone: a crawler reads thousands of pages (and, rendering them,
/// the API calls behind them) from a handful of addresses. A User-Agent is easy to fake, so the address is checked the way
/// the search engines ask: its reverse DNS name is in their domain, and that name resolves back to the same address.
/// Each address is checked once a day.
/// </summary>
public sealed class SearchCrawlers(IMemoryCache cache, IOptionsMonitor<ApiOptions> options, ILogger<SearchCrawlers> log)
{
    private const string VerifiedItem = "cp.search-crawler";

    /// <summary>User-Agent token → the domains its addresses' names end in.</summary>
    private static readonly (string Agent, string[] Domains)[] Known =
    [
        ("googlebot", [".googlebot.com", ".google.com"]),
        ("google-inspectiontool", [".googlebot.com", ".google.com"]),
        ("bingbot", [".search.msn.com"]),
        ("applebot", [".applebot.apple.com"]),
        ("yandexbot", [".yandex.ru", ".yandex.net", ".yandex.com"]),
    ];

    /// <summary>Whether this request was checked and came from a search engine's crawler (set by the middleware).</summary>
    public static bool IsVerified(HttpContext context) => context.Items.ContainsKey(VerifiedItem);

    public async Task CheckAsync(HttpContext context)
    {
        var agent = context.Request.Headers.UserAgent.ToString();
        var domains = Known.FirstOrDefault(k => agent.Contains(k.Agent, StringComparison.OrdinalIgnoreCase)).Domains;
        if (domains is null) return;
        if (!IPAddress.TryParse(ApiKeyValidator.ClientIp(context, options.CurrentValue.ClientIpHeader), out var ip)) return;

        var verified = await cache.GetOrCreateAsync($"crawler:{ip}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            entry.Size = 1;   // the shared cache has a size limit
            var ok = await VerifyAsync(ip, domains);
            if (!ok) log.LogInformation("{Address} says it is a search crawler ({Agent}) but its DNS doesn't agree", ip, agent);
            return ok;
        });
        if (verified) context.Items[VerifiedItem] = true;
    }

    private static async Task<bool> VerifyAsync(IPAddress ip, string[] domains)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var host = (await Dns.GetHostEntryAsync(ip.ToString(), timeout.Token)).HostName.TrimEnd('.');
            if (!domains.Any(d => host.EndsWith(d, StringComparison.OrdinalIgnoreCase))) return false;
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            return addresses.Any(a => Plain(a).Equals(Plain(ip)));
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
    }

    private static IPAddress Plain(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;
}

public static class SearchCrawlerExtensions
{
    /// <summary>Marks requests from verified search crawlers. Runs before the rate limiter.</summary>
    public static IApplicationBuilder UseSearchCrawlers(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        await context.RequestServices.GetRequiredService<SearchCrawlers>().CheckAsync(context);
        await next(context);
    });

    /// <summary>
    /// Keeps the API, the admin page and the API docs out of search results. They aren't blocked in robots.txt: search
    /// engines render the pages by running the app, which needs the API.
    /// </summary>
    public static IApplicationBuilder UseNoIndexForNonPages(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/admin") || path.StartsWithSegments("/swagger") ||
            path.StartsWithSegments("/openapi"))
            context.Response.Headers["X-Robots-Tag"] = "noindex";
        await next(context);
    });
}
