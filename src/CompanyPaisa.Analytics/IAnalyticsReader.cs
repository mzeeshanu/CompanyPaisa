namespace CompanyPaisa.Analytics;

/// <summary>What the private dashboard reads.</summary>
public interface IAnalyticsReader
{
    Task<AnalyticsReport> GetReportAsync(DateOnly from, DateOnly to, int top, CancellationToken ct);
    IAsyncEnumerable<AnalyticsEvent> GetEventsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
