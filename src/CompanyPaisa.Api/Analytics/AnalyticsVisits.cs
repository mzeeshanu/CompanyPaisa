using CompanyPaisa.Analytics;

namespace CompanyPaisa.Api.Analytics;

/// <summary>One thing a visitor did during a visit, in the order they did it.</summary>
public sealed record VisitStep(DateTimeOffset At, string Name, string? Subject, string? Label, string? Detail, string? Path);

/// <summary>
/// A run of events from one visitor (the day's visitor hash) with no gap longer than <see cref="AnalyticsVisits.Idle"/>.
/// The hash itself isn't sent: it's only needed to tie the events together.
/// </summary>
public sealed record Visit(
    DateTimeOffset Start, DateTimeOffset End, string? Country, string? Region, string? City, string Device, string Browser, string Os,
    string? Referrer, string Source, int Events, int Searches, int CompanyViews, int ExecutiveViews, IReadOnlyList<VisitStep> Steps);

/// <summary>The most recent visits first; <see cref="Total"/> counts every visit in the range, including ones not listed.</summary>
public sealed record VisitsResponse(int Total, IReadOnlyList<Visit> Visits);

public static class AnalyticsVisits
{
    /// <summary>After this long with nothing happening, the next event starts a new visit.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    /// <summary>Keeps a script hammering the public API from turning into one enormous timeline.</summary>
    public const int MaxSteps = 200;

    private static readonly HashSet<string> SearchEvents = new(StringComparer.Ordinal) { "search", "executive_search", "place_lookup", "name_search" };

    /// <summary>Groups events (in the order they happened) into visits and returns the newest <paramref name="limit"/>.</summary>
    public static async Task<VisitsResponse> BuildAsync(IAsyncEnumerable<AnalyticsEvent> events, int limit, CancellationToken ct)
    {
        var open = new Dictionary<(string Day, string Visitor), List<AnalyticsEvent>>();
        var done = new List<List<AnalyticsEvent>>();
        await foreach (var e in events.WithCancellation(ct))
        {
            var key = (e.Day, e.Visitor);
            if (open.TryGetValue(key, out var run) && e.OccurredAt - run[^1].OccurredAt <= Idle)
            {
                run.Add(e);
                continue;
            }
            if (run is not null) done.Add(run);
            open[key] = [e];
        }
        done.AddRange(open.Values);

        var visits = done.OrderByDescending(r => r[0].OccurredAt).Take(limit).Select(ToVisit).ToList();
        return new VisitsResponse(done.Count, visits);
    }

    private static Visit ToVisit(List<AnalyticsEvent> run)
    {
        var first = run[0];
        // Same browser and network all visit, so where and on what come from the first event; the rest may add what it lacks.
        return new Visit(
            first.OccurredAt, run[^1].OccurredAt,
            run.Select(e => e.Country).FirstOrDefault(v => v is not null),
            run.Select(e => e.Region).FirstOrDefault(v => v is not null),
            run.Select(e => e.City).FirstOrDefault(v => v is not null),
            first.Device, first.Browser, first.Os,
            run.Select(e => e.Referrer).FirstOrDefault(v => v is not null),
            run.Any(e => e.Source == "website") ? "website" : first.Source,
            run.Count,
            run.Count(e => SearchEvents.Contains(e.Name)),
            run.Count(e => e.Name == "company_view"),
            run.Count(e => e.Name == "executive_view"),
            run.Take(MaxSteps).Select(e => new VisitStep(e.OccurredAt, e.Name, e.Subject, e.Label, e.Detail, e.Path)).ToList());
    }
}
