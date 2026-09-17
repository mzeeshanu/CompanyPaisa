using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace CompanyPaisa.Analytics;

/// <summary>
/// The SQL shared by every database store. Only portable SQL (it runs unchanged on Postgres and SQLite), so moving the data
/// to another database is a dump and a restore. Subclasses supply the connection and the table definitions.
/// </summary>
public abstract class DbAnalyticsStore : IAnalyticsStore, IAnalyticsReader
{
    public abstract string Provider { get; }
    protected abstract ValueTask<DbConnection> OpenAsync(CancellationToken ct);
    protected abstract IEnumerable<string> SchemaStatements { get; }

    private const string Columns =
        "occurred_at, day, name, visitor, subject, label, detail, latitude, longitude, country, region, city, device, browser, os, referrer, path, source";

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        foreach (var sql in SchemaStatements) await ExecuteAsync(connection, sql, ct);
    }

    public async Task WriteAsync(IReadOnlyList<AnalyticsEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        await using var connection = await OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        foreach (var e in events)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO analytics_events ({Columns}) VALUES " +
                              "(@at, @day, @name, @visitor, @subject, @label, @detail, @lat, @lng, @country, @region, @city, @device, @browser, @os, @referrer, @path, @source)";
            Add(cmd, "at", e.OccurredAt.ToUniversalTime());
            Add(cmd, "day", e.Day);
            Add(cmd, "name", e.Name);
            Add(cmd, "visitor", e.Visitor);
            Add(cmd, "subject", e.Subject);
            Add(cmd, "label", e.Label);
            Add(cmd, "detail", e.Detail);
            Add(cmd, "lat", e.Latitude);
            Add(cmd, "lng", e.Longitude);
            Add(cmd, "country", e.Country);
            Add(cmd, "region", e.Region);
            Add(cmd, "city", e.City);
            Add(cmd, "device", e.Device);
            Add(cmd, "browser", e.Browser);
            Add(cmd, "os", e.Os);
            Add(cmd, "referrer", e.Referrer);
            Add(cmd, "path", e.Path);
            Add(cmd, "source", e.Source);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<string> GetDailySaltAsync(string day, CancellationToken ct)
    {
        var keepFrom = DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync(ct);
        await ExecuteAsync(connection, "DELETE FROM analytics_salts WHERE day < @keep", ct, ("keep", keepFrom));
        await ExecuteAsync(connection, "INSERT INTO analytics_salts (day, salt) VALUES (@day, @salt) ON CONFLICT (day) DO NOTHING", ct,
            ("day", day), ("salt", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
        await using var cmd = Command(connection, "SELECT salt FROM analytics_salts WHERE day = @day", ("day", day));
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<AnalyticsReport> GetReportAsync(DateOnly from, DateOnly to, int top, CancellationToken ct)
    {
        var range = new[] { ("from", (object?)Day(from)), ("to", Day(to)) };
        await using var connection = await OpenAsync(ct);

        const string sums =
            "COUNT(DISTINCT visitor), " +
            "SUM(CASE WHEN name = 'page_view' THEN 1 ELSE 0 END), " +
            "SUM(CASE WHEN name IN ('search', 'executive_search') THEN 1 ELSE 0 END), " +
            "SUM(CASE WHEN name = 'company_view' THEN 1 ELSE 0 END), " +
            "SUM(CASE WHEN name = 'executive_view' THEN 1 ELSE 0 END)";

        AnalyticsTotals totals;
        await using (var cmd = Command(connection, $"SELECT {sums}, COUNT(*) FROM analytics_events WHERE day >= @from AND day <= @to", range))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            totals = new AnalyticsTotals(Int(r, 0), Int(r, 1), Int(r, 2), Int(r, 3), Int(r, 4), Int(r, 5));
        }

        var days = new List<AnalyticsDay>();
        await using (var cmd = Command(connection, $"SELECT day, {sums} FROM analytics_events WHERE day >= @from AND day <= @to GROUP BY day ORDER BY day", range))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                days.Add(new AnalyticsDay(r.GetString(0), Int(r, 1), Int(r, 2), Int(r, 3), Int(r, 4), Int(r, 5)));

        var points = new List<AnalyticsCount>();
        await using (var cmd = Command(connection,
                         "SELECT latitude, longitude, MAX(label), COUNT(*), COUNT(DISTINCT visitor) FROM analytics_events " +
                         "WHERE day >= @from AND day <= @to AND name IN ('search', 'executive_search') AND latitude IS NOT NULL AND longitude IS NOT NULL " +
                         "GROUP BY latitude, longitude ORDER BY 5 DESC, 4 DESC LIMIT @top",
                         [.. range, ("top", top * 5)]))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                var lat = Convert.ToDouble(r.GetValue(0), CultureInfo.InvariantCulture);
                var lng = Convert.ToDouble(r.GetValue(1), CultureInfo.InvariantCulture);
                points.Add(new AnalyticsCount(string.Create(CultureInfo.InvariantCulture, $"{lat:0.00}, {lng:0.00}"),
                    r.IsDBNull(2) ? null : r.GetString(2), Int(r, 3), Int(r, 4), lat, lng));
            }

        Task<List<AnalyticsCount>> Top(string key, string? where = null, bool labelled = false) =>
            RankedAsync(connection, key, where, labelled, range, top, ct);

        var cities = (await Top("COALESCE(city, '') || '|' || COALESCE(region, '') || '|' || COALESCE(country, '')", "city IS NOT NULL"))
            .Select(c => c with { Key = string.Join(", ", c.Key.Split('|').Where(p => p.Length > 0)) }).ToList();

        return new AnalyticsReport(Day(from), Day(to), totals, days, points,
            await Top("subject", "name = 'place_lookup'", labelled: true),
            await Top("subject", "name = 'company_view'", labelled: true),
            await Top("subject", "name = 'executive_view'", labelled: true),
            await Top("country"),
            cities,
            await Top("device"),
            await Top("browser"),
            await Top("os"),
            await Top("referrer", "name = 'page_view'"),
            await Top("name"),
            await Top("source"));
    }

    public async IAsyncEnumerable<AnalyticsEvent> GetEventsAsync(DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var cmd = Command(connection, $"SELECT {Columns} FROM analytics_events WHERE day >= @from AND day <= @to ORDER BY id",
            ("from", Day(from)), ("to", Day(to)));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            yield return new AnalyticsEvent(
                r.GetFieldValue<DateTimeOffset>(0), r.GetString(2), r.GetString(3), Text(r, 4), Text(r, 5), Text(r, 6),
                Number(r, 7), Number(r, 8), Text(r, 9), Text(r, 10), Text(r, 11), r.GetString(12), r.GetString(13), r.GetString(14),
                Text(r, 15), Text(r, 16), r.GetString(17));
    }

    /// <summary>Top values of one column (a trusted SQL expression, never user input), most visitors first.</summary>
    private static async Task<List<AnalyticsCount>> RankedAsync(DbConnection connection, string key, string? where, bool labelled,
        (string, object?)[] range, int top, CancellationToken ct)
    {
        var sql = $"SELECT {key}, {(labelled ? "MAX(label)" : "NULL")}, COUNT(*), COUNT(DISTINCT visitor) FROM analytics_events " +
                  $"WHERE day >= @from AND day <= @to AND {key} IS NOT NULL{(where is null ? "" : $" AND {where}")} " +
                  $"GROUP BY {key} ORDER BY 4 DESC, 3 DESC LIMIT @top";
        var list = new List<AnalyticsCount>();
        await using var cmd = Command(connection, sql, [.. range, ("top", top)]);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new AnalyticsCount(r.GetString(0), Text(r, 1), Int(r, 2), Int(r, 3)));
        return list;
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static int Int(DbDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
    private static string? Text(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static double? Number(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i), CultureInfo.InvariantCulture);

    private static DbCommand Command(DbConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) Add(cmd, name, value);
        return cmd;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var cmd = Command(connection, sql, parameters);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void Add(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@" + name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
