using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Analytics;

/// <summary>Where <c>Analytics:ImportFrom</c> says the history is: an adapter and its settings (that same section).</summary>
public sealed record AnalyticsImportSource(IAnalyticsAdapter Adapter, IConfigurationSection Settings);

/// <summary>
/// Moving to another provider without losing the history. With <c>Analytics:ImportFrom:Provider</c> set (plus that adapter's
/// own settings under <c>Analytics:ImportFrom</c>, e.g. <c>Analytics:ImportFrom:ConnectionString</c>), the writer copies every
/// event from that store into the current one before it writes anything new — only when the current store has no events
/// yet, so it runs once and a restart never duplicates. All or nothing: events are read in full, then written in one
/// transaction, so a failed copy writes nothing. Reading is retried for about a minute (a database waking up); after that the
/// site records new events without the history, and the log says how to try again. Daily salts aren't copied, so a visitor
/// seen on the day of the move can count twice that day.
/// </summary>
public sealed class AnalyticsImport(IServiceProvider services, ILogger<AnalyticsImport> logger, AnalyticsImportSource? source,
    IReadOnlyList<TimeSpan>? retries = null)
{
    public const string SectionName = "ImportFrom";

    /// <summary>Waits between attempts to read the source (a sleeping or restarting database).</summary>
    private readonly IReadOnlyList<TimeSpan> _retries = retries ?? [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)];

    /// <summary>Events copied by the last run (null when it didn't copy).</summary>
    public int? Copied { get; private set; }

    public async Task RunAsync(IAnalyticsStore target, CancellationToken ct)
    {
        if (source is null) return;
        var name = source.Adapter.Name;
        if (source.Adapter.Problem(source.Settings) is { } problem)
        {
            logger.LogWarning("Analytics:ImportFrom is {Provider} but not usable ({Problem}); nothing copied", name, problem);
            return;
        }
        if (await target.HasEventsAsync(ct))
        {
            logger.LogInformation("Analytics:ImportFrom is set, but {Target} already has events; nothing copied. Remove the ImportFrom settings.",
                target.Provider);
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var events = await ReadAllAsync(ct);
                await target.WriteAsync(events, ct);
                Copied = events.Count;
                logger.LogInformation("Copied {Count} analytics events from {Source} into {Target}. Remove the ImportFrom settings now.",
                    events.Count, name, target.Provider);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt >= _retries.Count)
                {
                    logger.LogError(ex, "Couldn't copy analytics events from {Source}; nothing was copied, and {Target} now records "
                                        + "without the history, which is still in the source. To try again: fix the ImportFrom settings, "
                                        + "empty the current store (for Sqlite, delete the file) and restart.", name, target.Provider);
                    return;
                }
                logger.LogWarning(ex, "Reading analytics events from {Source} failed; retrying", name);
                await Task.Delay(_retries[attempt], ct);
            }
        }
    }

    private async Task<List<AnalyticsEvent>> ReadAllAsync(CancellationToken ct)
    {
        var database = source!.Adapter.Create(services, source.Settings);
        try
        {
            var events = new List<AnalyticsEvent>();
            await foreach (var e in database.GetEventsAsync(DateOnly.MinValue, DateOnly.MaxValue, ct)) events.Add(e);
            return events;
        }
        finally
        {
            if (database is IAsyncDisposable a) await a.DisposeAsync();
            else if (database is IDisposable d) d.Dispose();
        }
    }
}
