using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Analytics;

/// <summary>appsettings section "Analytics".</summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>"None", "Sqlite" (a local file — development) or "Postgres" (production).</summary>
    [RegularExpression("^(?i)(None|Sqlite|Postgres)$")] public string Provider { get; set; } = "None";

    /// <summary>Postgres: a connection string or a postgresql:// URL (Railway's DATABASE_URL). Keep it in environment variables.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Sqlite: the file, relative to the app's content root. Keep it out of data/*.db, which ships with the app.</summary>
    public string SqlitePath { get; set; } = "../../data/analytics/analytics.db";

    /// <summary>Secret for the private dashboard (/admin). Empty = dashboard off. Keep it in environment variables.</summary>
    [MinLength(16)] public string? DashboardKey { get; set; }

    [Range(100, 1_000_000)] public int QueueCapacity { get; set; } = 10_000;
    [Range(1, 5_000)] public int BatchSize { get; set; } = 200;
    /// <summary>How long the writer waits to gather a batch.</summary>
    [Range(10, 600_000)] public int FlushIntervalMs { get; set; } = 2_000;

    /// <summary>Visitor location headers added by the CDN (Cloudflare: country always; city and region with "Add visitor location headers").</summary>
    public string CountryHeader { get; set; } = "CF-IPCountry";
    public string RegionHeader { get; set; } = "cf-region";
    public string CityHeader { get; set; } = "cf-ipcity";

    /// <summary>Browsers with this cookie (set from the dashboard) aren't counted — the site owner's own visits.</summary>
    public string OwnerCookie { get; set; } = "cp_owner";
}

/// <summary>Whether analytics are recording, and why not.</summary>
public sealed record AnalyticsStatus(string Provider, bool Enabled, string Message);
