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
