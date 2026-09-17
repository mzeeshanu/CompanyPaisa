using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Analytics;

/// <summary>
/// One stored event. <see cref="Visitor"/> is a hash that changes every day (from a random daily salt that is thrown away),
/// so a visitor can be counted once per day but never followed across days, and their IP address is never stored.
/// </summary>
public sealed record AnalyticsEvent(
    DateTimeOffset OccurredAt,
    string Name,
    string Visitor,
    string? Subject,
    string? Label,
    string? Detail,
    double? Latitude,
    double? Longitude,
    string? Country,
    string? Region,
    string? City,
    string Device,
    string Browser,
    string Os,
    string? Referrer,
    string? Path,
    string Source)
{
    /// <summary>The UTC day ("2026-09-16") the event is filed under.</summary>
    public string Day => OccurredAt.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// An event waiting in memory to be written. <see cref="Fingerprint"/> (IP + browser) only lives here: the writer turns it
/// into the day's visitor hash and discards it.
/// </summary>
public sealed record PendingAnalyticsEvent(AnalyticsEvent Event, string Fingerprint);

/// <summary>Whether analytics are recording, and why not.</summary>
public sealed record AnalyticsStatus(string Provider, bool Enabled, string Message);

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

// ---------- The dashboard's report ----------

/// <param name="Visitors">Distinct visitors per day, added up over the range (someone who came on two days counts twice).</param>
public sealed record AnalyticsTotals(int Visitors, int PageViews, int Searches, int CompanyViews, int ExecutiveViews, int Events);

public sealed record AnalyticsDay(string Day, int Visitors, int PageViews, int Searches, int CompanyViews, int ExecutiveViews);

/// <summary>A row of a "top" list: how often, and by how many visitors (per day, added up).</summary>
public sealed record AnalyticsCount(string Key, string? Label, int Count, int Visitors, double? Latitude = null, double? Longitude = null);

public sealed record AnalyticsReport(
    string From,
    string To,
    AnalyticsTotals Totals,
    IReadOnlyList<AnalyticsDay> Days,
    IReadOnlyList<AnalyticsCount> SearchPoints,
    IReadOnlyList<AnalyticsCount> Places,
    IReadOnlyList<AnalyticsCount> Companies,
    IReadOnlyList<AnalyticsCount> Executives,
    IReadOnlyList<AnalyticsCount> Countries,
    IReadOnlyList<AnalyticsCount> Cities,
    IReadOnlyList<AnalyticsCount> Devices,
    IReadOnlyList<AnalyticsCount> Browsers,
    IReadOnlyList<AnalyticsCount> OperatingSystems,
    IReadOnlyList<AnalyticsCount> Referrers,
    IReadOnlyList<AnalyticsCount> Actions,
    IReadOnlyList<AnalyticsCount> Sources);

// ---------- Swappable storage ----------

/// <summary>Where events are kept. Implementations: <see cref="PostgresAnalyticsStore"/>, <see cref="SqliteAnalyticsStore"/>.</summary>
public interface IAnalyticsStore
{
    string Provider { get; }
    /// <summary>Creates the tables if they don't exist.</summary>
    Task InitializeAsync(CancellationToken ct);
    Task WriteAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct);
    /// <summary>The random salt for a UTC day, created on first use; salts older than the day before are deleted.</summary>
    Task<string> GetDailySaltAsync(string day, CancellationToken ct);
}

/// <summary>What the private dashboard reads.</summary>
public interface IAnalyticsReader
{
    Task<AnalyticsReport> GetReportAsync(DateOnly from, DateOnly to, int top, CancellationToken ct);
    IAsyncEnumerable<AnalyticsEvent> GetEventsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>Accepts events without waiting; false when the queue is full (the event is dropped).</summary>
public interface IAnalyticsSink
{
    bool TryWrite(PendingAnalyticsEvent pending);
}
