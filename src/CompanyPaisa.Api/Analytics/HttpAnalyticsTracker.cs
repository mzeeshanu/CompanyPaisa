using System.Text.Json;
using CompanyPaisa.Analytics;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Api.Security;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Analytics;

/// <summary>
/// Adds the "who, where and on what" from the current HTTP request and queues the event. Stores no IP address and sets no
/// cookie: the IP and browser only go into the day's visitor hash. Skips bots and the site owner's own browser.
/// </summary>
public sealed class HttpAnalyticsTracker(
    IHttpContextAccessor http,
    IAnalyticsSink sink,
    IApiKeyValidator keys,
    IOptionsMonitor<AnalyticsOptions> analytics,
    IOptionsMonitor<ApiOptions> api,
    IClock clock) : IAnalyticsTracker
{
    public void Track(AnalyticsAction action) => Track(action, referrer: null, path: null);

    /// <summary>Also records where the visitor came from and which page (for page views sent by the website).</summary>
    public void Track(AnalyticsAction action, string? referrer, string? path)
    {
        if (http.HttpContext is not { } context) return;
        var o = analytics.CurrentValue;
        var request = context.Request;
        if (request.Cookies.ContainsKey(o.OwnerCookie)) return;

        var userAgent = request.Headers.UserAgent.ToString();
        var ua = UserAgentInfo.Parse(userAgent);
        if (ua.IsBot) return;

        // The website's own calls come from the same origin; anything else is someone using the public API.
        var source = keys.Identify(context).IsKeyed ? "api-key"
            : request.Headers["Sec-Fetch-Site"].ToString() == "same-origin" ? "website" : "api";

        var detail = action.Detail?.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => Clip(kv.Value, 200));
        var e = new AnalyticsEvent(
            clock.UtcNow, action.Name, Visitor: "", Clip(action.Subject, 200), Clip(action.Label, 200),
            detail is { Count: > 0 } ? JsonSerializer.Serialize(detail) : null,
            action.Latitude, action.Longitude,
            Country(Header(request, o.CountryHeader)), Header(request, o.RegionHeader), Header(request, o.CityHeader),
            ua.Device, ua.Browser, ua.Os, Clip(referrer, 200), Clip(path, 200), source);

        var ip = ApiKeyValidator.ClientIp(context, api.CurrentValue.ClientIpHeader);
        sink.TryWrite(new PendingAnalyticsEvent(e, $"{ip}|{userAgent}"));
    }

    /// <summary>Cloudflare uses "XX" for unknown and "T1" for Tor.</summary>
    private static string? Country(string? code) => code is null or "XX" or "T1" ? null : code.ToUpperInvariant();

    private static string? Header(HttpRequest request, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var value = request.Headers[name].ToString().Trim();
        if (value.Length == 0) return null;
        if (value.Contains('%'))
            try { value = Uri.UnescapeDataString(value); } catch (UriFormatException) { }
        return Clip(value, 100);
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value.Trim() : value[..max].Trim();
}
