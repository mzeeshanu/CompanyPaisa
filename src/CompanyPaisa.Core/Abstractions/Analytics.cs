namespace CompanyPaisa.Core.Abstractions;

/// <summary>
/// Something a visitor did ("search", "company_view"…), before the web layer adds when, from where and on what device.
/// <paramref name="Subject"/> is what it was about (a ticker, a person id), <paramref name="Label"/> its readable name.
/// </summary>
public sealed record AnalyticsAction(
    string Name,
    string? Subject = null,
    string? Label = null,
    IReadOnlyDictionary<string, string?>? Detail = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Records visitor actions for the site's own analytics. Implementations must never throw or make a request wait
/// (they queue and write in the background). The default does nothing.
/// </summary>
public interface IAnalyticsTracker
{
    void Track(AnalyticsAction action);
}

public sealed class NullAnalyticsTracker : IAnalyticsTracker
{
    public void Track(AnalyticsAction action) { }
}
