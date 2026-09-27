namespace CompanyPaisa.Analytics;

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
