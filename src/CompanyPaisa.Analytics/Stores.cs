using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace CompanyPaisa.Analytics;

/// <summary>Production store: a Postgres database (Railway today; Azure Database for PostgreSQL moves with pg_dump / pg_restore).</summary>
public sealed class PostgresAnalyticsStore(string connectionString) : DbAnalyticsStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(PostgresConnectionString.From(connectionString));

    public override string Provider => "Postgres";

    protected override async ValueTask<DbConnection> OpenAsync(CancellationToken ct) => await _dataSource.OpenConnectionAsync(ct);

    protected override IEnumerable<string> SchemaStatements =>
    [
        """
        CREATE TABLE IF NOT EXISTS analytics_events (
            id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            occurred_at TIMESTAMPTZ NOT NULL, day TEXT NOT NULL, name TEXT NOT NULL, visitor TEXT NOT NULL,
            subject TEXT, label TEXT, detail TEXT, latitude DOUBLE PRECISION, longitude DOUBLE PRECISION,
            country TEXT, region TEXT, city TEXT, device TEXT NOT NULL, browser TEXT NOT NULL, os TEXT NOT NULL,
            referrer TEXT, path TEXT, source TEXT NOT NULL)
        """,
        "CREATE INDEX IF NOT EXISTS ix_analytics_events_day_name ON analytics_events (day, name)",
        "CREATE TABLE IF NOT EXISTS analytics_salts (day TEXT PRIMARY KEY, salt TEXT NOT NULL)"
    ];

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}

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

/// <summary>Railway (and Heroku, Render…) hand out "postgresql://user:pass@host:port/db"; Npgsql wants "Host=…;Username=…".</summary>
public static class PostgresConnectionString
{
    public static string From(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
        };
        var sslMode = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) && p.Length == 2)?[1];
        if (sslMode is not null && Enum.TryParse<SslMode>(sslMode.Replace("-", ""), ignoreCase: true, out var mode)) builder.SslMode = mode;
        return builder.ConnectionString;
    }
}
