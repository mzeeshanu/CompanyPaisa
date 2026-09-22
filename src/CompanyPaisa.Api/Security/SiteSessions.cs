using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CompanyPaisa.Api.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Security;

/// <summary>
/// The website's own pass to the API. Every page the site serves sets a short-lived cookie that only this server can sign;
/// the API accepts calls that carry it (from the site's own pages) or an API key. Other apps and scripts need a key.
/// The cookie holds only its expiry time and a signature: nothing that identifies the visitor, so it isn't tracking.
/// It isn't a wall — anyone can load a page and reuse its cookie — but it keeps the API from being an open public
/// endpoint, and the rate limits and bot rules make copying the data set slow.
/// </summary>
public sealed class SiteSessions
{
    private readonly IOptionsMonitor<ApiOptions> _options;
    private readonly TimeProvider _time;
    private readonly byte[] _secret;

    public SiteSessions(IOptionsMonitor<ApiOptions> options, TimeProvider time, IHostEnvironment environment, ILogger<SiteSessions> logger)
    {
        _options = options;
        _time = time;
        var configured = options.CurrentValue.SiteSession.Secret;
        if (string.IsNullOrWhiteSpace(configured))
        {
            // Passes then only last until the app restarts; the website fetches a new one when that happens.
            _secret = RandomNumberGenerator.GetBytes(32);
            if (environment.IsProduction())
                logger.LogWarning("Api:SiteSession:Secret isn't set; website passes reset on every restart. Set it to a long random value.");
        }
        else _secret = Encoding.UTF8.GetBytes(configured);
    }

    public string CookieName => _options.CurrentValue.SiteSession.CookieName;

    /// <summary>A valid pass on the request, and whether it is past half its life (then it is renewed).</summary>
    public (bool Valid, bool Ageing) Check(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value)) return (false, false);
        var dot = value.IndexOf('.');
        if (dot <= 0 || !long.TryParse(value.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var expires)) return (false, false);
        var expected = Sign(expires);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value[(dot + 1)..]), Encoding.ASCII.GetBytes(expected))) return (false, false);
        var left = DateTimeOffset.FromUnixTimeSeconds(expires) - _time.GetUtcNow();
        if (left <= TimeSpan.Zero) return (false, false);
        return (true, left < TimeSpan.FromHours(_options.CurrentValue.SiteSession.Hours) / 2);
    }

    /// <summary>Sets a fresh pass (HttpOnly, sent only to /api on this site).</summary>
    public void Issue(HttpContext context)
    {
        var expires = _time.GetUtcNow().AddHours(_options.CurrentValue.SiteSession.Hours);
        var seconds = expires.ToUnixTimeSeconds();
        context.Response.Cookies.Append(CookieName, $"{seconds.ToString(CultureInfo.InvariantCulture)}.{Sign(seconds)}", new CookieOptions
        {
            HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/api", Expires = expires, IsEssential = true
        });
    }

    private string Sign(long expires)
    {
        var mac = HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(expires.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToBase64String(mac, 0, 18).Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Whether the call came from one of the site's own pages: browsers say where a request comes from (Sec-Fetch-Site),
    /// and another website's page ("cross-site") doesn't count. Browsers too old to say are let through.
    /// </summary>
    public static bool FromOwnPage(HttpContext context) =>
        context.Request.Headers["Sec-Fetch-Site"].ToString() is "" or "same-origin" or "none";

    /// <summary>A page view (not an API call, a script, a stylesheet or an image): the moment to hand out a pass.</summary>
    public static bool IsPageRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;
        var path = request.Path.Value ?? "/";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) return false;
        var extension = Path.GetExtension(path);
        return extension.Length == 0 || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Automated clients (scripts, scrapers, headless browsers) by their User-Agent; they need an API key.</summary>
public static class BotRules
{
    public static bool IsBlocked(HttpContext context, ApiOptions options)
    {
        var agent = context.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(agent)) return true;
        return options.BlockedUserAgents.Any(b => agent.Contains(b, StringComparison.OrdinalIgnoreCase));
    }
}

public static class SiteSessionExtensions
{
    /// <summary>Hands the website's pages their API pass. Runs before static files, so the home page gets one too.</summary>
    public static IApplicationBuilder UseSiteSessions(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (SiteSessions.IsPageRequest(context.Request))
        {
            var sessions = context.RequestServices.GetRequiredService<SiteSessions>();
            if (sessions.Check(context) is not (true, false)) sessions.Issue(context);
        }
        await next(context);
    });

    /// <summary>
    /// GET /api/session: a new pass for a page that has been open longer than its pass lasts (the website calls it when the
    /// API says the pass has run out). Only for the site's own pages and ordinary browsers.
    /// </summary>
    public static IEndpointRouteBuilder MapSiteSession(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/session", (HttpContext context, SiteSessions sessions, IOptionsMonitor<ApiOptions> options) =>
            {
                if (!SiteSessions.FromOwnPage(context) || BotRules.IsBlocked(context, options.CurrentValue))
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not available",
                        detail: "Apps and scripts need an API key: send it in the 'X-Api-Key' header.");
                sessions.Issue(context);
                context.Response.Headers.CacheControl = "no-store";
                return Results.NoContent();
            })
            .RequireRateLimiting(Endpoints.V1Endpoints.RateLimitPolicy)
            .ExcludeFromDescription();
        return app;
    }
}
