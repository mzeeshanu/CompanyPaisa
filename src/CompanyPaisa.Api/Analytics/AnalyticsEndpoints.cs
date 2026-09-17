using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CompanyPaisa.Analytics;
using CompanyPaisa.Api.Endpoints;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Analytics;

/// <summary>What the website reports that the API can't see by itself (page views and clicks).</summary>
public sealed record ClientEventRequest(string Name, string? Subject = null, string? Path = null, string? Referrer = null);

/// <summary>The dashboard's data: whether analytics are on, the report, and searches grouped by the nearest town.</summary>
public sealed record AnalyticsDashboardResponse(AnalyticsStatus Status, AnalyticsReport? Report, IReadOnlyList<AnalyticsCount> Areas);

public static class AnalyticsEndpoints
{
    /// <summary>Header carrying Analytics:DashboardKey.</summary>
    public const string AdminKeyHeader = "X-Admin-Key";

    /// <summary>Events the website may send. Searches and company / executive views are recorded by the API itself.</summary>
    public static readonly HashSet<string> ClientEvents = new(StringComparer.Ordinal)
    {
        "page_view", "location_gps", "location_zip", "location_area", "location_link", "name_search", "view_map", "view_list", "mode_companies", "mode_executives",
        "fact_next", "fact_info", "executives_more", "about_open", "privacy_open", "report_open"
    };

    public static IEndpointRouteBuilder MapCompanyPaisaAnalytics(this IEndpointRouteBuilder app)
    {
        // ----- From the website -----
        app.MapPost("/api/v1/events", (ClientEventRequest body, HttpContext context, IServiceProvider services) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name) || !ClientEvents.Contains(body.Name))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown event",
                        detail: $"Use one of: {string.Join(", ", ClientEvents.Order())}.");
                // Off (no store configured): accept and forget, so the website never shows an error.
                if (services.GetService<IAnalyticsTracker>() is HttpAnalyticsTracker tracker)
                    tracker.Track(new AnalyticsAction(body.Name, body.Subject), ExternalReferrer(body.Referrer, context), PathOnly(body.Path));
                return Results.NoContent();
            })
            .RequireRateLimiting(V1Endpoints.RateLimitPolicy)
            .ExcludeFromDescription();

        // ----- Private dashboard (/admin) -----
        var admin = app.MapGroup("/api/admin/analytics")
            .AddEndpointFilter(RequireDashboardKey)
            .RequireRateLimiting(V1Endpoints.RateLimitPolicy)
            .ExcludeFromDescription();

        admin.MapGet("/report", async (int? days, AnalyticsStatus status, IServiceProvider services, IReverseGeoLocator places, CancellationToken ct) =>
        {
            var (from, to) = Range(days);
            if (!status.Enabled || services.GetService<IAnalyticsReader>() is not { } reader)
                return Results.Ok(new AnalyticsDashboardResponse(status, null, []));
            var report = await reader.GetReportAsync(from, to, top: 25, ct);
            return Results.Ok(new AnalyticsDashboardResponse(status, report, await AreasAsync(report.SearchPoints, places, ct)));
        });

        admin.MapGet("/events.csv", async (int? days, AnalyticsStatus status, IServiceProvider services, HttpContext context, CancellationToken ct) =>
        {
            if (!status.Enabled || services.GetService<IAnalyticsReader>() is not { } reader) return Results.NotFound();
            var (from, to) = Range(days);
            context.Response.ContentType = "text/csv; charset=utf-8";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"companypaisa-events-{from:yyyyMMdd}-{to:yyyyMMdd}.csv\"";
            await using var writer = new StreamWriter(context.Response.Body, new UTF8Encoding(false), bufferSize: 16_384, leaveOpen: true);
            await writer.WriteLineAsync("occurred_at,day,name,visitor,subject,label,detail,latitude,longitude,country,region,city,device,browser,os,referrer,path,source");
            await foreach (var e in reader.GetEventsAsync(from, to, ct))
                await writer.WriteLineAsync(string.Join(',', new[]
                {
                    e.OccurredAt.ToString("O", CultureInfo.InvariantCulture), e.Day, e.Name, e.Visitor, e.Subject, e.Label, e.Detail,
                    e.Latitude?.ToString(CultureInfo.InvariantCulture), e.Longitude?.ToString(CultureInfo.InvariantCulture),
                    e.Country, e.Region, e.City, e.Device, e.Browser, e.Os, e.Referrer, e.Path, e.Source
                }.Select(Csv)));
            return Results.Empty;
        });

        // "Don't count me": a cookie on the owner's own browsers, set from the dashboard.
        admin.MapPost("/owner", (HttpContext context, IOptionsMonitor<AnalyticsOptions> o) =>
        {
            context.Response.Cookies.Append(o.CurrentValue.OwnerCookie, "1", new CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddDays(400)
            });
            return Results.NoContent();
        });
        admin.MapDelete("/owner", (HttpContext context, IOptionsMonitor<AnalyticsOptions> o) =>
        {
            context.Response.Cookies.Delete(o.CurrentValue.OwnerCookie);
            return Results.NoContent();
        });
        admin.MapGet("/owner", (HttpContext context, IOptionsMonitor<AnalyticsOptions> o) =>
            Results.Ok(new { excluded = context.Request.Cookies.ContainsKey(o.CurrentValue.OwnerCookie) }));

        return app;
    }

    /// <summary>404 while no dashboard key is configured (the dashboard doesn't exist), 401 for a wrong key.</summary>
    private static async ValueTask<object?> RequireDashboardKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var key = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<AnalyticsOptions>>().CurrentValue.DashboardKey;
        if (string.IsNullOrWhiteSpace(key)) return Results.NotFound();
        var presented = context.HttpContext.Request.Headers[AdminKeyHeader].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(key)))
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong dashboard key");
        return await next(context);
    }

    /// <summary>The last <paramref name="days"/> UTC days including today (default 30, at most a year and a bit).</summary>
    private static (DateOnly From, DateOnly To) Range(int? days)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return (to.AddDays(-(Math.Clamp(days ?? 30, 1, 400) - 1)), to);
    }

    /// <summary>Search points (rounded to ~1 km) named after the nearest town and merged, so "Palo Alto, CA" is one row.</summary>
    private static async Task<IReadOnlyList<AnalyticsCount>> AreasAsync(IReadOnlyList<AnalyticsCount> points, IReverseGeoLocator places, CancellationToken ct)
    {
        var areas = new Dictionary<string, AnalyticsCount>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in points)
        {
            var place = p is { Latitude: { } lat, Longitude: { } lng } ? await places.NearestCityAsync(new GeoPoint(lat, lng), 30, ct) : null;
            var name = place is null ? $"Near {p.Key}" : $"{place.City}, {place.State}";
            areas[name] = areas.TryGetValue(name, out var a)
                ? a with { Count = a.Count + p.Count, Visitors = a.Visitors + p.Visitors }
                : new AnalyticsCount(name, null, p.Count, p.Visitors, p.Latitude, p.Longitude);
        }
        return areas.Values.OrderByDescending(a => a.Visitors).ThenByDescending(a => a.Count).Take(25).ToList();
    }

    /// <summary>Just the other site's host ("google.com"); nothing for internal navigation.</summary>
    private static string? ExternalReferrer(string? referrer, HttpContext context)
    {
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var own = context.Request.Host.Host;
        return host.Equals(own, StringComparison.OrdinalIgnoreCase) || own.EndsWith("." + host, StringComparison.OrdinalIgnoreCase) ? null : host.ToLowerInvariant();
    }

    private static string? PathOnly(string? path) =>
        string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') ? null : path.Split('?', '#')[0];

    private static string Csv(string? value) =>
        value is null ? "" : value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
