namespace CompanyPaisa.Analytics;

/// <summary>Where events are kept. Implementations come from adapters (<see cref="IAnalyticsAdapter"/>).</summary>
public interface IAnalyticsStore
{
    string Provider { get; }
    /// <summary>Creates the tables if they don't exist.</summary>
    Task InitializeAsync(CancellationToken ct);
    /// <summary>Writes the events in one transaction: all of them or none.</summary>
    Task WriteAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct);
    /// <summary>Whether any event has been stored yet.</summary>
    Task<bool> HasEventsAsync(CancellationToken ct);
    /// <summary>The random salt for a UTC day, created on first use; salts older than the day before are deleted.</summary>
    Task<string> GetDailySaltAsync(string day, CancellationToken ct);
}
