using System.ComponentModel.DataAnnotations;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Api.Options;

/// <summary>appsettings section "Api".</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// When true, callers without a key are allowed (at the anonymous rate limit) — for development, where the React dev
    /// server serves the pages. Off, only the website's own pages (their site pass) and apps with a key get in.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    [Required] public string ApiKeyHeader { get; set; } = "X-Api-Key";

    /// <summary>The website's own pass to the API (see SiteSessions).</summary>
    public SiteSessionOptions SiteSession { get; set; } = new();

    /// <summary>
    /// User-Agent fragments of scripts and scrapers; without an API key they get 403 (and no User-Agent at all is treated
    /// the same). Browsers and search-engine crawlers don't call the API.
    /// </summary>
    public List<string> BlockedUserAgents { get; set; } =
    [
        "curl", "wget", "python-requests", "python-urllib", "python-httpx", "aiohttp", "httpx", "go-http-client", "java/", "okhttp",
        "libwww-perl", "scrapy", "node-fetch", "undici", "axios", "headlesschrome", "phantomjs", "puppeteer", "playwright", "selenium",
        "httpclient", "postmanruntime", "insomnia", "powershell", "winhttp", "gptbot", "claudebot", "ccbot", "bytespider", "petalbot"
    ];

    /// <summary>Keys for external apps. Put real keys in user-secrets / environment variables, never in appsettings.json.</summary>
    public List<ApiClientKey> Keys { get; set; } = [];

    public RateLimitOptions RateLimits { get; set; } = new();

    /// <summary>
    /// Header a CDN in front of the site sets to the visitor's own IP (Cloudflare: "CF-Connecting-IP"). Without it, anonymous
    /// rate limits would be per CDN edge server — everyone behind one Cloudflare data centre sharing a single limit.
    /// Empty = use the connection's address.
    /// </summary>
    public string? ClientIpHeader { get; set; }

    /// <summary>Origins (other websites) allowed to call the API from a browser. The site itself doesn't need an entry.</summary>
    public List<string> CorsAllowedOrigins { get; set; } = [];

    public bool EnableOpenApi { get; set; } = true;
    public bool EnableSwaggerUi { get; set; } = true;
}

public sealed class ApiClientKey
{
    [Required] public string Name { get; set; } = "";
    [Required, MinLength(16)] public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class RateLimitOptions
{
    [Range(1, 1_000_000)] public int AnonymousPerMinute { get; set; } = 120;
    [Range(1, 1_000_000)] public int KeyedPerMinute { get; set; } = 1200;
    /// <summary>
    /// API calls one visitor's IP address may make in an hour without a key: plenty for a person (a company page is four
    /// calls), slow going for someone copying all 5,000 companies.
    /// </summary>
    [Range(1, 10_000_000)] public int AnonymousPerHour { get; set; } = 2000;
    /// <summary>Server-rendered pages (company, executive, search) one IP address may fetch in an hour.</summary>
    [Range(1, 10_000_000)] public int PagesPerHour { get; set; } = 1200;
}

public sealed class SiteSessionOptions
{
    /// <summary>Signs the website's passes (32+ characters). Set it in the host's secrets so passes survive a restart.</summary>
    public string? Secret { get; set; }
    [Required] public string CookieName { get; set; } = "cp_site";
    /// <summary>How long a pass lasts; it is renewed while the site is in use.</summary>
    [Range(1, 168)] public int Hours { get; set; } = 12;
}

/// <summary>appsettings section "Hosting" — how the app sits behind a platform like Railway.</summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Trust X-Forwarded-For / X-Forwarded-Proto from the platform's proxy, so the app sees the visitor's
    /// real IP (rate limits) and the original https scheme (no redirect loops). Only enable behind a proxy.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }

    /// <summary>Redirect http → https outside Development.</summary>
    public bool UseHttpsRedirection { get; set; } = true;
}

/// <summary>appsettings section "Ui" — defaults sent to the website via /api/v1/client-config.</summary>
public sealed class UiOptions
{
    public const string SectionName = "Ui";

    [RegularExpression("^(List|Map)$")] public string DefaultView { get; set; } = "List";
    [RegularExpression("^(Auto|Light|Dark)$")] public string DefaultTheme { get; set; } = "Auto";
    public MapUiOptions Map { get; set; } = new();
    public ConsentUiOptions Consent { get; set; } = new();
    /// <summary>Metros the data set covers, shown on the location screen. Keep in step with the importer's "Regions".</summary>
    public List<CoverageAreaOptions> Coverage { get; set; } = [];
    /// <summary>
    /// Where people can ask for a correction or exercise data-protection rights (an email address or a URL),
    /// shown in the privacy notice. Empty = the notice says a contact route is coming.
    /// </summary>
    public string PrivacyContact { get; set; } = "";
}

public sealed class CoverageAreaOptions
{
    [Required] public string Name { get; set; } = "";
    /// <summary>A US ZIP ("84043"), a UK postcode district ("EC2N"), a Canadian postal area ("M5J") or a European postcode ("75008", Dutch "1012").</summary>
    [RegularExpression(@"^(\d{4,5}|[A-Z]{1,2}\d[A-Z\d]?)$")] public string ExampleZip { get; set; } = "";
    [RegularExpression("^(US|CA|UK|FR|NL|IT|ES|AU|NZ|PK)$")] public string Country { get; set; } = "US";
}

public sealed class MapUiOptions
{
    public bool ShowBaseMapByDefault { get; set; } = true;
    /// <summary>Self-hosted .pmtiles file (Phase 4). Null = use the simplified built-in map.</summary>
    public string? TilesUrl { get; set; }
}

public sealed class ConsentUiOptions
{
    [Required] public string CookieName { get; set; } = "cp_prefs";
    [Range(1, 730)] public int CookieDays { get; set; } = 365;
}

/// <summary>appsettings section "Features" — on/off switches, e.g. { "MapView": true, "Executives": true }.</summary>
public sealed class FeatureOptions : Dictionary<string, bool>
{
    public const string SectionName = "Features";
    public FeatureOptions() : base(StringComparer.OrdinalIgnoreCase) { }
    public bool IsEnabled(string feature) => TryGetValue(feature, out var on) && on;
}

internal static class UiOptionsExtensions
{
    public static ClientConfigDto ToClientConfig(this UiOptions ui, Core.Options.SearchOptions search, FeatureOptions features) => new(
        ui.DefaultView, ui.DefaultTheme, search.DefaultRadiusMiles, search.AllowedRadiiMiles, search.DefaultSort,
        ui.Map.ShowBaseMapByDefault, ui.Map.TilesUrl, ui.Consent.CookieName, ui.Consent.CookieDays,
        new Dictionary<string, bool>(features, StringComparer.OrdinalIgnoreCase),
        ui.Coverage.Select(c => new CoverageAreaDto(c.Name, c.ExampleZip, c.Country)).ToList(),
        string.IsNullOrWhiteSpace(ui.PrivacyContact) ? null : ui.PrivacyContact.Trim());
}
