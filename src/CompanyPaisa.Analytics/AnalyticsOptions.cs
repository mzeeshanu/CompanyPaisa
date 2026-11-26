using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Analytics;

/// <summary>appsettings section "Analytics".</summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>
    /// Which registered adapter keeps the events ("None", or an adapter's name such as "Sqlite" or "Postgres"; see
    /// <see cref="AnalyticsAdapters"/>). Each adapter reads its own settings from this section (Sqlite: SqlitePath;
    /// Postgres: ConnectionString).
    /// </summary>
    [Required] public string Provider { get; set; } = AnalyticsAdapters.None;

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
