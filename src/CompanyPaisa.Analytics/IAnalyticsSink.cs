namespace CompanyPaisa.Analytics;

/// <summary>Accepts events without waiting; false when the queue is full (the event is dropped).</summary>
public interface IAnalyticsSink
{
    bool TryWrite(PendingAnalyticsEvent pending);
}
