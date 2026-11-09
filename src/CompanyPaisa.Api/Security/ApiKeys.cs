using System.Security.Cryptography;
using System.Text;
using CompanyPaisa.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Security;

/// <summary>Who is calling: a named API client, an anonymous caller, or someone with a bad key.</summary>
public sealed record ApiCaller(string? ClientName, bool KeyPresented, bool KeyValid, string PartitionKey)
{
    public bool IsKeyed => ClientName is not null;
}

public interface IApiKeyValidator
{
    ApiCaller Identify(HttpContext context);
}

public sealed class ApiKeyValidator(IOptionsMonitor<ApiOptions> options) : IApiKeyValidator
{
    public ApiCaller Identify(HttpContext context)
    {
        var o = options.CurrentValue;
        if (context.Items.TryGetValue(typeof(ApiCaller), out var cached) && cached is ApiCaller known) return known;

        ApiCaller caller;
        var ip = ClientIp(context, o);
        var presented = context.Request.Headers[o.ApiKeyHeader].ToString();
        if (string.IsNullOrEmpty(presented))
        {
            caller = new ApiCaller(null, false, false, $"ip:{ip}");
        }
        else
        {
            var match = o.Keys.FirstOrDefault(k => k.Enabled && FixedTimeEquals(k.Key, presented));
            caller = match is null
                ? new ApiCaller(null, true, false, $"badkey:{ip}")
                : new ApiCaller(match.Name, true, true, $"key:{match.Name}");
        }

        context.Items[typeof(ApiCaller)] = caller;
        return caller;
    }

    /// <summary>The visitor's IP as the API's options say to read it (see <see cref="ApiOptions.ClientIpHeaderOnlyFromCloudflare"/>).</summary>
    public static string ClientIp(HttpContext context, ApiOptions o) => ClientIp(context, o.ClientIpHeader, o.ClientIpHeaderOnlyFromCloudflare);

    /// <summary>
    /// The visitor's IP: from the CDN's header when configured and valid, else the connection's address. Anyone reaching
    /// the server without going through the CDN can send that header too — with a search engine's address to skip the
    /// hourly caps — so a header on a connection from outside Cloudflare's networks is logged, and with
    /// <paramref name="onlyFromCloudflare"/> ignored.
    /// </summary>
    public static string ClientIp(HttpContext context, string? header, bool onlyFromCloudflare = false)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (!string.IsNullOrWhiteSpace(header) && context.Request.Headers[header].ToString() is { Length: > 0 } value &&
            System.Net.IPAddress.TryParse(value.Split(',')[0].Trim(), out var fromCdn))
        {
            if (remote is not null && Cloudflare.Contains(remote)) return fromCdn.ToString();
            ReportOutsideCloudflare(context, header, remote);
            if (!onlyFromCloudflare) return fromCdn.ToString();
        }
        return remote?.ToString() ?? "unknown";
    }

    /// <summary>At most one warning every ten minutes: enough to see whether real traffic would be affected.</summary>
    private static void ReportOutsideCloudflare(HttpContext context, string header, System.Net.IPAddress? remote)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastReport);
        if (last != 0 && now - last < 600_000 || Interlocked.CompareExchange(ref _lastReport, now, last) != last) return;
        (context.RequestServices?.GetService<ILoggerFactory>())?.CreateLogger(typeof(ApiKeyValidator))
            .LogWarning("{Header} sent from {Remote}, which is not a Cloudflare address", header, remote?.ToString() ?? "unknown");
    }

    private static long _lastReport;

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

/// <summary>Cloudflare's published networks (cloudflare.com/ips, October 2026).</summary>
public static class Cloudflare
{
    private static readonly System.Net.IPNetwork[] Networks =
    [
        .. new[]
        {
            "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18", "108.162.192.0/18",
            "190.93.240.0/20", "188.114.96.0/20", "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
            "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
            "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32"
        }.Select(System.Net.IPNetwork.Parse)
    ];

    public static bool Contains(System.Net.IPAddress address)
    {
        var a = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return Networks.Any(n => n.Contains(a));
    }
}

/// <summary>
/// Who may call the API: an app with a valid key; otherwise only the website's own pages (their <see cref="SiteSessions"/>
/// pass), and never a script or scraper without a key. With Api:AllowAnonymous on (development), anyone.
/// </summary>
public sealed class ApiKeyEndpointFilter(IApiKeyValidator validator, SiteSessions sessions, IOptionsMonitor<ApiOptions> options) : IEndpointFilter
{
    /// <summary>Problem "code" the website reacts to by fetching a new pass and trying once more.</summary>
    public const string SessionRequired = "site_session_required";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var o = options.CurrentValue;
        var http = context.HttpContext;
        var caller = validator.Identify(http);
        if (caller.KeyPresented && !caller.KeyValid)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid API key",
                detail: $"The key in the '{o.ApiKeyHeader}' header isn't recognised.");
        if (caller.IsKeyed || o.AllowAnonymous) return await next(context);

        if (BotRules.IsBlocked(http, o))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "API key required",
                detail: $"Apps and scripts need an API key: send it in the '{o.ApiKeyHeader}' header.");

        var (valid, ageing) = sessions.Check(http);
        if (!valid || !SiteSessions.FromOwnPage(http))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "API key required",
                detail: $"Send your key in the '{o.ApiKeyHeader}' header.",
                extensions: new Dictionary<string, object?> { ["code"] = SessionRequired });
        if (ageing) sessions.Issue(http);
        return await next(context);
    }
}
