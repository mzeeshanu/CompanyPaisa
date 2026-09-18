using System.Globalization;
using System.Text.Json;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using Microsoft.Data.Sqlite;

namespace CompanyPaisa.Data.Sqlite;

/// <summary>
/// The published data set as one SQLite file. Every row belongs to a market — the importer run that produced it
/// ("sec", "uk", "eu"…) — so each import replaces only its own rows. Filing URLs are stored once in their own table
/// (a quarter of a million rows point at about a hundred thousand filings), with their common prefixes shortened.
/// </summary>
public static class SqliteDataStore
{
    /// <summary>Bumped whenever the table layout changes; an older file is refused rather than misread.</summary>
    public const int SchemaVersion = 1;

    // Amounts are NUMERIC: whole numbers (nearly all of them) are stored as compact integers, fractions (EPS) as REAL.
    // No per-market indexes: replacing a market scans each table once, which takes well under a second.
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (market TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (market, key));
        CREATE TABLE IF NOT EXISTS filings (id INTEGER PRIMARY KEY, url TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS companies (
            company_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, market TEXT NOT NULL,
            name TEXT NOT NULL, ticker TEXT NOT NULL, exchange TEXT NOT NULL, sector TEXT NOT NULL, industry TEXT, website TEXT,
            employees INTEGER, market_cap NUMERIC, description TEXT, currency TEXT NOT NULL, pay_currency TEXT,
            fiscal_year_end TEXT, logo_url TEXT, as_of_date TEXT);
        CREATE TABLE IF NOT EXISTS locations (
            location_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, company_id TEXT NOT NULL, market TEXT NOT NULL,
            type TEXT NOT NULL, label TEXT NOT NULL, street TEXT NOT NULL, city TEXT NOT NULL, state TEXT NOT NULL,
            postal_code TEXT NOT NULL, latitude REAL NOT NULL, longitude REAL NOT NULL);
        CREATE TABLE IF NOT EXISTS financials (
            company_id TEXT NOT NULL, market TEXT NOT NULL, period_type TEXT NOT NULL, fiscal_year INTEGER NOT NULL,
            fiscal_quarter INTEGER, revenue NUMERIC NOT NULL, net_income NUMERIC NOT NULL, operating_income NUMERIC, eps NUMERIC,
            filing_id INTEGER REFERENCES filings(id));
        CREATE TABLE IF NOT EXISTS executive_compensation (
            company_id TEXT NOT NULL, market TEXT NOT NULL, person_id TEXT NOT NULL, exec_name TEXT NOT NULL, title TEXT NOT NULL,
            year INTEGER NOT NULL, salary NUMERIC NOT NULL, bonus NUMERIC NOT NULL, stock_awards NUMERIC NOT NULL, other NUMERIC NOT NULL,
            total NUMERIC NOT NULL, filing_id INTEGER REFERENCES filings(id));
        CREATE TABLE IF NOT EXISTS people (
            person_id TEXT NOT NULL COLLATE NOCASE, market TEXT NOT NULL, name TEXT NOT NULL, sec_cik TEXT,
            PRIMARY KEY (person_id, market));
        CREATE TABLE IF NOT EXISTS new_executives (
            company_id TEXT NOT NULL, market TEXT NOT NULL, person_id TEXT, name TEXT NOT NULL, title TEXT NOT NULL,
            announced_on TEXT NOT NULL, starts_on TEXT, filing_id INTEGER REFERENCES filings(id), package TEXT NOT NULL);
        """;

    private static readonly JsonSerializerOptions PackageJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Common URL starts stored as short tokens (112,000 filing links share a handful of prefixes).</summary>
    private static readonly (string Token, string Prefix)[] UrlPrefixes =
    [
        ("~sec/", "https://www.sec.gov/Archives/edgar/data/"),
        ("~xbrl/", "https://filings.xbrl.org/")
    ];

    private static string Shorten(string url)
    {
        foreach (var (token, prefix) in UrlPrefixes)
            if (url.StartsWith(prefix, StringComparison.Ordinal)) return token + url[prefix.Length..];
        return url;
    }

    private static string Expand(string stored)
    {
        foreach (var (token, prefix) in UrlPrefixes)
            if (stored.StartsWith(token, StringComparison.Ordinal)) return prefix + stored[token.Length..];
        return stored;
    }

    private static readonly string[] Tables = ["companies", "locations", "financials", "executive_compensation", "people", "new_executives", "meta"];

    /// <summary>
    /// Replaces one market's rows. Works on a copy that is read back and checked with the API's own rules before it
    /// replaces the file, so a failed import never leaves a half-written or invalid database behind.
    /// </summary>
    /// <param name="meta">data_version, as_of_date, is_sample, source… for this market.</param>
    public static void ReplaceMarket(string path, string market, CompanyData data, IReadOnlyDictionary<string, string> meta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var staging = path + ".new";
        if (File.Exists(staging)) File.Delete(staging);
        if (File.Exists(path)) File.Copy(path, staging);
        try
        {
            WriteStaging(staging, path, market, data, meta);
            DataRules.Check(Read(staging, DateTimeOffset.UtcNow), staging);
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);   // only left when something failed
        }
    }

    private static void WriteStaging(string staging, string path, string market, CompanyData data, IReadOnlyDictionary<string, string> meta)
    {
        using (var db = Open(staging, readOnly: false))
        {
            var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"), CultureInfo.InvariantCulture);
            if (version != 0 && version != SchemaVersion)
                throw new InvalidOperationException($"'{path}' uses database layout {version}, this code writes {SchemaVersion}. " +
                                                    "Rebuild it: delete the file and run every importer (or --migrate-xlsx).");
            Execute(db, Schema);
            Execute(db, $"PRAGMA user_version = {SchemaVersion}");
            using (var tx = db.BeginTransaction())
            {
                foreach (var table in Tables) Execute(db, $"DELETE FROM {table} WHERE market = $m", ("$m", market));
                Insert(db, market, data, meta);
                Execute(db, UnusedFilings);
                tx.Commit();
            }
            Execute(db, "VACUUM");
        }
    }

    private const string UnusedFilings = """
        DELETE FROM filings WHERE id NOT IN (
            SELECT filing_id FROM financials WHERE filing_id IS NOT NULL
            UNION SELECT filing_id FROM executive_compensation WHERE filing_id IS NOT NULL
            UNION SELECT filing_id FROM new_executives WHERE filing_id IS NOT NULL)
        """;

    /// <summary>
    /// Replaces one market's officer appointments only, leaving its companies and pay as they are (a separate, quicker
    /// import step). Same safety as <see cref="ReplaceMarket"/>: written to a copy, checked, then swapped in.
    /// </summary>
    public static void ReplaceNewExecutives(string path, string market, IReadOnlyList<NewExecutive> rows)
    {
        var staging = path + ".new";
        if (File.Exists(staging)) File.Delete(staging);
        File.Copy(path, staging);
        try
        {
            using (var db = Open(staging, readOnly: false))
            {
                Execute(db, Schema);
                using (var tx = db.BeginTransaction())
                {
                    Execute(db, "DELETE FROM new_executives WHERE market = $m", ("$m", market));
                    InsertNewExecutives(db, market, rows, FilingIds(db));
                    Execute(db, UnusedFilings);
                    tx.Commit();
                }
                Execute(db, "VACUUM");
            }
            DataRules.Check(Read(staging, DateTimeOffset.UtcNow), staging);
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    /// <summary>Every market's rows as one data set; metadata combined across markets.</summary>
    public static CompanyData Read(string path, DateTimeOffset loadedAt)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Database not found at '{path}'. Check DataSource:Sqlite:Path in appsettings.", path);
        using var db = Open(path, readOnly: true);
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version != SchemaVersion)
            throw new DataLoadException(path, [$"Database layout {version}; this version of the site reads layout {SchemaVersion}. Re-run the importers."]);
        var filings = Query(db, "SELECT id, url FROM filings", r => (Id: r.GetInt64(0), Url: Expand(r.GetString(1)))).ToDictionary(x => x.Id, x => x.Url);
        string? Filing(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : filings.GetValueOrDefault(r.GetInt64(i));

        var companies = Query(db, """
            SELECT company_id, name, ticker, exchange, sector, industry, website, employees, market_cap, description,
                   currency, pay_currency, fiscal_year_end, logo_url, as_of_date FROM companies ORDER BY rowid
            """, r => new Company
        {
            CompanyId = r.GetString(0), Name = r.GetString(1), Ticker = r.GetString(2), Exchange = r.GetString(3), Sector = r.GetString(4),
            Industry = Text(r, 5), Website = Text(r, 6), Employees = r.IsDBNull(7) ? null : r.GetInt32(7), MarketCap = Money(r, 8),
            Description = Text(r, 9), Currency = r.GetString(10), PayCurrency = Text(r, 11), FiscalYearEnd = Text(r, 12), LogoUrl = Text(r, 13),
            AsOfDate = Text(r, 14) is { } d ? DateOnly.Parse(d, CultureInfo.InvariantCulture) : null
        });
        var locations = Query(db, """
            SELECT location_id, company_id, type, label, street, city, state, postal_code, latitude, longitude FROM locations ORDER BY rowid
            """, r => new CompanyLocation
        {
            LocationId = r.GetString(0), CompanyId = r.GetString(1), Type = Enum.Parse<LocationType>(r.GetString(2)), Label = r.GetString(3),
            Street = r.GetString(4), City = r.GetString(5), State = r.GetString(6), PostalCode = r.GetString(7),
            Point = new GeoPoint(r.GetDouble(8), r.GetDouble(9))
        });
        var financials = Query(db, """
            SELECT company_id, period_type, fiscal_year, fiscal_quarter, revenue, net_income, operating_income, eps, filing_id
            FROM financials ORDER BY rowid
            """, r => new FinancialPeriod
        {
            CompanyId = r.GetString(0), PeriodType = Enum.Parse<PeriodType>(r.GetString(1)), FiscalYear = r.GetInt32(2),
            FiscalQuarter = r.IsDBNull(3) ? null : r.GetInt32(3), Revenue = Money(r, 4)!.Value, NetIncome = Money(r, 5)!.Value,
            OperatingIncome = Money(r, 6), Eps = Money(r, 7), SourceFiling = Filing(r, 8)
        });
        var pay = Query(db, """
            SELECT company_id, person_id, exec_name, title, year, salary, bonus, stock_awards, other, total, filing_id
            FROM executive_compensation ORDER BY rowid
            """, r => new ExecutiveCompensation
        {
            CompanyId = r.GetString(0), PersonId = r.GetString(1), ExecutiveName = r.GetString(2), Title = r.GetString(3), Year = r.GetInt32(4),
            Salary = Money(r, 5)!.Value, Bonus = Money(r, 6)!.Value, StockAwards = Money(r, 7)!.Value, Other = Money(r, 8)!.Value,
            Total = Money(r, 9)!.Value, SourceFiling = Filing(r, 10)
        });
        // Files written before appointments were collected have no such table.
        var appointments = Convert.ToInt64(Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'new_executives'"), CultureInfo.InvariantCulture) == 0
            ? []
            : Query(db, """
                SELECT company_id, person_id, name, title, announced_on, starts_on, filing_id, package FROM new_executives ORDER BY rowid
                """, r => new NewExecutive
            {
                CompanyId = r.GetString(0), PersonId = Text(r, 1), Name = r.GetString(2), Title = r.GetString(3),
                AnnouncedOn = DateOnly.Parse(r.GetString(4), CultureInfo.InvariantCulture),
                StartsOn = Text(r, 5) is { } s ? DateOnly.Parse(s, CultureInfo.InvariantCulture) : null,
                SourceFiling = Filing(r, 6),
                Package = JsonSerializer.Deserialize<List<PackageItem>>(r.GetString(7), PackageJson) ?? []
            });
        var people = Query(db, "SELECT person_id, name, sec_cik FROM people ORDER BY rowid",
                r => new Person { PersonId = r.GetString(0), Name = r.GetString(1), SecCik = Text(r, 2) })
            .DistinctBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase).ToList();

        // One row per market and key; the data set's version lists every market's.
        var meta = Query(db, "SELECT market, key, value FROM meta ORDER BY market", r => (Market: r.GetString(0), Key: r.GetString(1), Value: r.GetString(2)))
            .GroupBy(x => x.Market).Select(g => g.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).ToList();
        var metadata = new DataSetMetadata(
            meta.Count == 0 ? "unversioned" : string.Join('+', meta.Select(m => m.GetValueOrDefault("data_version", "unversioned"))),
            meta.Select(m => DateOnly.TryParse(m.GetValueOrDefault("as_of_date"), CultureInfo.InvariantCulture, out var d) ? d : (DateOnly?)null).Where(d => d is not null).Min(),
            meta.Any(m => bool.TryParse(m.GetValueOrDefault("is_sample"), out var s) && s),
            loadedAt);

        return new CompanyData(companies, locations, financials, pay, people, metadata, appointments);
    }

    /// <summary>The markets in the file and each one's metadata (for reports).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Markets(string path)
    {
        using var db = Open(path, readOnly: true);
        return Query(db, "SELECT market, key, value FROM meta", r => (Market: r.GetString(0), Key: r.GetString(1), Value: r.GetString(2)))
            .GroupBy(x => x.Market)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(x => x.Key, x => x.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static void Insert(SqliteConnection db, string market, CompanyData data, IReadOnlyDictionary<string, string> meta)
    {
        using (var cmd = Command(db, "INSERT INTO meta (market, key, value) VALUES ($market, $0, $1)", market, 2))
            foreach (var (k, v) in meta) Run(cmd, k, v);

        var filings = FilingIds(db);
        object FilingId(string? url) => filings(url);

        using (var cmd = Command(db, """
            INSERT INTO companies (company_id, market, name, ticker, exchange, sector, industry, website, employees, market_cap, description,
                                   currency, pay_currency, fiscal_year_end, logo_url, as_of_date)
            VALUES ($0, $market, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
            """, market, 15))
            foreach (var c in data.Companies)
                Run(cmd, c.CompanyId, c.Name, c.Ticker, c.Exchange, c.Sector, c.Industry, c.Website, c.Employees, c.MarketCap, c.Description,
                    c.Currency, c.PayCurrency, c.FiscalYearEnd, c.LogoUrl, c.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        using (var cmd = Command(db, """
            INSERT INTO locations (location_id, company_id, market, type, label, street, city, state, postal_code, latitude, longitude)
            VALUES ($0, $1, $market, $2, $3, $4, $5, $6, $7, $8, $9)
            """, market, 10))
            foreach (var l in data.Locations)
                Run(cmd, l.LocationId, l.CompanyId, l.Type.ToString(), l.Label, l.Street, l.City, l.State, l.PostalCode, l.Point.Latitude, l.Point.Longitude);

        using (var cmd = Command(db, """
            INSERT INTO financials (company_id, market, period_type, fiscal_year, fiscal_quarter, revenue, net_income, operating_income, eps, filing_id)
            VALUES ($0, $market, $1, $2, $3, $4, $5, $6, $7, $8)
            """, market, 9))
            foreach (var f in data.Financials)
                Run(cmd, f.CompanyId, f.PeriodType.ToString(), f.FiscalYear, f.FiscalQuarter, f.Revenue, f.NetIncome, f.OperatingIncome, f.Eps, FilingId(f.SourceFiling));

        using (var cmd = Command(db, """
            INSERT INTO executive_compensation (company_id, market, person_id, exec_name, title, year, salary, bonus, stock_awards, other, total, filing_id)
            VALUES ($0, $market, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """, market, 11))
            foreach (var e in data.Pay)
                Run(cmd, e.CompanyId, e.PersonId, e.ExecutiveName, e.Title, e.Year, e.Salary, e.Bonus, e.StockAwards, e.Other, e.Total, FilingId(e.SourceFiling));

        using (var cmd = Command(db, "INSERT OR IGNORE INTO people (person_id, market, name, sec_cik) VALUES ($0, $market, $1, $2)", market, 3))
            foreach (var p in data.People) Run(cmd, p.PersonId, p.Name, p.SecCik);

        InsertNewExecutives(db, market, data.Appointments, filings);
    }

    private static void InsertNewExecutives(SqliteConnection db, string market, IReadOnlyList<NewExecutive> rows, Func<string?, object> filingId)
    {
        using var cmd = Command(db, """
            INSERT INTO new_executives (company_id, market, person_id, name, title, announced_on, starts_on, filing_id, package)
            VALUES ($0, $market, $1, $2, $3, $4, $5, $6, $7)
            """, market, 8);
        foreach (var e in rows)
            Run(cmd, e.CompanyId, e.PersonId, e.Name, e.Title, e.AnnouncedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.StartsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filingId(e.SourceFiling),
                JsonSerializer.Serialize(e.Package, PackageJson));
    }

    /// <summary>Filing ids by URL; filings other markets already use are shared (matched in memory, not through an index).</summary>
    private static Func<string?, object> FilingIds(SqliteConnection db)
    {
        var filingIds = Query(db, "SELECT id, url FROM filings", r => (Id: r.GetInt64(0), Url: r.GetString(1)))
            .GroupBy(x => x.Url, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var addFiling = Command(db, "INSERT INTO filings (url) VALUES ($0) RETURNING id", null, 1);
        return url =>
        {
            if (string.IsNullOrEmpty(url)) return DBNull.Value;
            var stored = Shorten(url);
            if (filingIds.TryGetValue(stored, out var id)) return id;
            id = (long)(Run(addFiling, stored) ?? throw new InvalidOperationException("No filing id returned."));
            filingIds[stored] = id;
            return id;
        };
    }

    // Pooling off: no connection keeps the file open, so the importer can replace it while the website is running.
    private static SqliteConnection Open(string path, bool readOnly)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        db.Open();
        return db;
    }

    /// <summary>A prepared command with parameters $0…$n-1 (and $market when given).</summary>
    private static SqliteCommand Command(SqliteConnection db, string sql, string? market, int parameters)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        for (var i = 0; i < parameters; i++) cmd.Parameters.Add(new SqliteParameter($"${i}", null));
        if (market is not null) cmd.Parameters.AddWithValue("$market", market);
        return cmd;
    }

    private static object? Run(SqliteCommand cmd, params object?[] values)
    {
        // decimal would be bound as TEXT; amounts are REAL columns.
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] switch { null => DBNull.Value, decimal d => (double)d, var v => v };
        return cmd.CommandText.Contains("RETURNING", StringComparison.Ordinal) ? cmd.ExecuteScalar() : cmd.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection db, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static List<T> Query<T>(SqliteConnection db, string sql, Func<SqliteDataReader, T> map)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>Amounts come back as decimal: whole ones exactly (stored as integers), fractions from REAL.</summary>
    private static decimal? Money(SqliteDataReader r, int i) => r.GetValue(i) switch
    {
        DBNull => null,
        long whole => whole,
        double d => (decimal)d,
        var other => Convert.ToDecimal(other, CultureInfo.InvariantCulture)
    };
}
