using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Analytics;

/// <summary>Events wait here in memory; a full queue drops new events rather than slow the site down.</summary>
public sealed class AnalyticsQueue(IOptions<AnalyticsOptions> options) : IAnalyticsSink
{
    private readonly Channel<PendingAnalyticsEvent> _channel = Channel.CreateBounded<PendingAnalyticsEvent>(
        new BoundedChannelOptions(options.Value.QueueCapacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<PendingAnalyticsEvent> Reader => _channel.Reader;

    public bool TryWrite(PendingAnalyticsEvent pending) => _channel.Writer.TryWrite(pending);
}

/// <summary>
/// Writes queued events in batches. Replaces each event's fingerprint (IP + browser, never stored) with a hash salted by
/// that day's random salt. The store keeps salts for two days at most, so older hashes can't be linked back to anyone.
/// </summary>
public sealed class AnalyticsWriter(AnalyticsQueue queue, IAnalyticsStore store, IOptions<AnalyticsOptions> options, ILogger<AnalyticsWriter> logger)
    : BackgroundService
{
    private readonly Dictionary<string, string> _salts = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await InitializeAsync(stoppingToken)) return;
        logger.LogInformation("Analytics recording to {Provider}", store.Provider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await queue.Reader.WaitToReadAsync(stoppingToken)) break;
                await Task.Delay(options.Value.FlushIntervalMs, stoppingToken);   // let a batch gather
            }
            catch (OperationCanceledException) { break; }
            await DrainAsync(CancellationToken.None);
        }
        await DrainAsync(CancellationToken.None);   // shutting down: write what's left
    }

    /// <summary>The database may not be reachable yet (a fresh deploy); keep trying without stopping the site.</summary>
    private async Task<bool> InitializeAsync(CancellationToken ct)
    {
        for (var attempt = 1; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await store.InitializeAsync(ct);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Log(attempt == 1 ? LogLevel.Warning : LogLevel.Debug, ex, "Analytics store ({Provider}) isn't reachable yet; retrying", store.Provider);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, attempt * 5)), ct); } catch (OperationCanceledException) { }
            }
        }
        return false;
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var batch = new List<PendingAnalyticsEvent>(options.Value.BatchSize);
        while (queue.Reader.TryRead(out var pending))
        {
            batch.Add(pending);
            if (batch.Count >= options.Value.BatchSize) { await WriteAsync(batch, ct); batch.Clear(); }
        }
        if (batch.Count > 0) await WriteAsync(batch, ct);
    }

    private async Task WriteAsync(List<PendingAnalyticsEvent> batch, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var rows = new List<AnalyticsEvent>(batch.Count);
                foreach (var p in batch)
                    rows.Add(p.Event with { Visitor = VisitorHash(await SaltAsync(p.Event.Day, ct), p.Fingerprint) });
                await store.WriteAsync(rows, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == 3)
                {
                    logger.LogError(ex, "Dropped {Count} analytics events after 3 attempts", batch.Count);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
    }

    private async Task<string> SaltAsync(string day, CancellationToken ct)
    {
        if (_salts.TryGetValue(day, out var salt)) return salt;
        salt = await store.GetDailySaltAsync(day, ct);
        foreach (var old in _salts.Keys.Where(k => string.CompareOrdinal(k, day) < 0).ToList()) _salts.Remove(old);
        return _salts[day] = salt;
    }

    /// <summary>32 hex characters; the same visitor gets the same value all day and a different one tomorrow.</summary>
    public static string VisitorHash(string salt, string fingerprint) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}|{fingerprint}")))[..32];
}
