using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace CompanyPaisa.Analytics;

/// <summary>Development store: one SQLite file on this PC, same tables and queries as Postgres.</summary>
public sealed class SqliteAnalyticsStore : DbAnalyticsStore
{
    private readonly string _connectionString;

    public SqliteAnalyticsStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    public string Path { get; }
    public override string Provider => "Sqlite";

    protected override async ValueTask<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    protected override IEnumerable<string> SchemaStatements =>
    [
        "PRAGMA journal_mode = WAL",
        """
        CREATE TABLE IF NOT EXISTS analytics_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            occurred_at TEXT NOT NULL, day TEXT NOT NULL, name TEXT NOT NULL, visitor TEXT NOT NULL,
            subject TEXT, label TEXT, detail TEXT, latitude REAL, longitude REAL,
            country TEXT, region TEXT, city TEXT, device TEXT NOT NULL, browser TEXT NOT NULL, os TEXT NOT NULL,
            referrer TEXT, path TEXT, source TEXT NOT NULL)
        """,
        "CREATE INDEX IF NOT EXISTS ix_analytics_events_day_name ON analytics_events (day, name)",
        "CREATE TABLE IF NOT EXISTS analytics_salts (day TEXT PRIMARY KEY, salt TEXT NOT NULL)"
    ];
}
