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
    /// <remarks>
    /// 2: companies.careers_url. 3: worker_pay (median employee and CEO pay ratio), job_salaries and extras. 4:
    /// job_salaries.source (visa filings or job ads) and .url. An older file is upgraded in place when an importer writes to it.
    /// </remarks>
    public const int SchemaVersion = 4;

    // Amounts are NUMERIC: whole numbers (nearly all of them) are stored as compact integers, fractions (EPS) as REAL.
    // No per-market indexes: replacing a market scans each table once, which takes well under a second.
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (market TEXT NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY (market, key));
        CREATE TABLE IF NOT EXISTS filings (id INTEGER PRIMARY KEY, url TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS companies (
            company_id TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, market TEXT NOT NULL,
            name TEXT NOT NULL, ticker TEXT NOT NULL, exchange TEXT NOT NULL, sector TEXT NOT NULL, industry TEXT, website TEXT,
            careers_url TEXT, employees INTEGER, market_cap NUMERIC, description TEXT, currency TEXT NOT NULL, pay_currency TEXT,
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
        CREATE TABLE IF NOT EXISTS worker_pay (
            company_id TEXT NOT NULL, market TEXT NOT NULL, year INTEGER NOT NULL, median_pay NUMERIC NOT NULL, ceo_pay NUMERIC NOT NULL,
            ratio NUMERIC NOT NULL, filing_id INTEGER REFERENCES filings(id));
        CREATE TABLE IF NOT EXISTS job_salaries (
            company_id TEXT NOT NULL, title TEXT NOT NULL, occupation TEXT, city TEXT, state TEXT, latitude REAL, longitude REAL,
            filings INTEGER NOT NULL, low NUMERIC NOT NULL, median NUMERIC NOT NULL, high NUMERIC NOT NULL, min NUMERIC NOT NULL, max NUMERIC NOT NULL,
            source TEXT NOT NULL DEFAULT 'h1b', url TEXT);
        CREATE TABLE IF NOT EXISTS extras (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);
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

    /// <summary>The tables whose rows belong to a market. job_salaries doesn't: the --salaries step replaces it whole.</summary>
    private static readonly string[] Tables = ["companies", "locations", "financials", "executive_compensation", "people", "new_executives", "worker_pay", "meta"];

    /// <summary>A company a market no longer lists takes its job salaries with it.</summary>
    private const string OrphanSalaries = "DELETE FROM job_salaries WHERE company_id COLLATE NOCASE NOT IN (SELECT company_id FROM companies)";

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
            Prepare(db, path);
            using (var tx = db.BeginTransaction())
            {
                foreach (var table in Tables) Execute(db, $"DELETE FROM {table} WHERE market = $m", ("$m", market));
                Insert(db, market, data, meta);
                Execute(db, OrphanSalaries);
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
            UNION SELECT filing_id FROM new_executives WHERE filing_id IS NOT NULL
            UNION SELECT filing_id FROM worker_pay WHERE filing_id IS NOT NULL)
        """;

    /// <summary>
    /// Replaces one source's job salaries (visa filings or job ads; they come from national sources, not from a market's
    /// import), keeping only rows for companies in the file. Same safety as <see cref="ReplaceMarket"/>: written to a copy, checked, then swapped in.
    /// </summary>
    public static void ReplaceJobSalaries(string path, IReadOnlyList<JobSalary> rows, JobSalarySource source)
    {
        var staging = path + ".new";
        if (File.Exists(staging)) File.Delete(staging);
        File.Copy(path, staging);
        try
        {
            using (var db = Open(staging, readOnly: false))
            {
                Prepare(db, path);
                using (var tx = db.BeginTransaction())
                {
                    Execute(db, "DELETE FROM job_salaries WHERE source = $s", ("$s", source.Kind));
                    using (var cmd = Command(db, """
                        INSERT INTO job_salaries (company_id, title, occupation, city, state, latitude, longitude, filings, low, median, high, min, max, source, url)
                        VALUES ($0, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
                        """, null, 15))
                        foreach (var j in rows)
                            Run(cmd, j.CompanyId, j.Title, j.Occupation, j.City, j.State, j.Point?.Latitude, j.Point?.Longitude,
                                j.Filings, j.Low, j.Median, j.High, j.Min, j.Max, source.Kind, j.Url);
                    Execute(db, OrphanSalaries);
                    Execute(db, "DELETE FROM extras WHERE key LIKE $k", ("$k", $"job_salaries.{source.Kind}.%"));
                    if (source.Kind == JobSalary.VisaFilings) Execute(db, "DELETE FROM extras WHERE key IN ('job_salaries.from', 'job_salaries.to', 'job_salaries.source')");
                    using (var cmd = Command(db, "INSERT INTO extras (key, value) VALUES ($0, $1)", null, 2))
                    {
                        Run(cmd, $"job_salaries.{source.Kind}.from", source.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        Run(cmd, $"job_salaries.{source.Kind}.to", source.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        Run(cmd, $"job_salaries.{source.Kind}.source", source.Source);
                    }
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
                Prepare(db, path);
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

    /// <summary>
    /// Creates the tables, or brings an older layout up to date (layout 1 → 2 adds companies.careers_url; 2 → 3 only adds
    /// tables, which the CREATE IF NOT EXISTS schema does).
    /// </summary>
    private static void Prepare(SqliteConnection db, string path)
    {
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version is not (0 or 1 or 2 or 3) && version != SchemaVersion)
            throw new InvalidOperationException($"'{path}' uses database layout {version}, this code writes {SchemaVersion}. " +
                                                "Rebuild it: delete the file and run every importer (or --migrate-xlsx).");
        Execute(db, Schema);
        if (version == 1) Execute(db, "ALTER TABLE companies ADD COLUMN careers_url TEXT");
        if (version == 3)
        {
            Execute(db, "ALTER TABLE job_salaries ADD COLUMN source TEXT NOT NULL DEFAULT 'h1b'");
            Execute(db, "ALTER TABLE job_salaries ADD COLUMN url TEXT");
        }
        Execute(db, $"PRAGMA user_version = {SchemaVersion}");
    }

    /// <summary>Brings an existing file to the current layout in place (adds the columns a newer layout has; data untouched).</summary>
    public static void Upgrade(string path)
    {
        using var db = Open(path, readOnly: false);
        Prepare(db, path);
    }

    /// <summary>
    /// Fills in company websites and careers pages, and moves locations to their geocoded street address, across every
    /// market (from the enrichment tables in data/reference). Same safety net as publishing: a checked copy replaces the file.
    /// </summary>
    public static void ApplyCompanyDetails(string path, IReadOnlyList<(string CompanyId, string? Website, string? CareersUrl)> sites,
        IReadOnlyList<(string LocationId, double Latitude, double Longitude)> points)
    {
        var staging = path + ".new";
        if (File.Exists(staging)) File.Delete(staging);
        File.Copy(path, staging);
        try
        {
            using (var db = Open(staging, readOnly: false))
            {
                Prepare(db, path);
                using (var tx = db.BeginTransaction())
                {
                    using (var cmd = Command(db, "UPDATE companies SET website = COALESCE(website, $0), careers_url = COALESCE($1, careers_url) WHERE company_id = $2", null, 3))
                        foreach (var s in sites) Run(cmd, s.Website, s.CareersUrl, s.CompanyId);
                    using (var cmd = Command(db, "UPDATE locations SET latitude = $0, longitude = $1 WHERE location_id = $2", null, 3))
                        foreach (var p in points) Run(cmd, p.Latitude, p.Longitude, p.LocationId);
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
                   currency, pay_currency, fiscal_year_end, logo_url, as_of_date, careers_url FROM companies ORDER BY rowid
            """, r => new Company
        {
            CompanyId = r.GetString(0), Name = r.GetString(1), Ticker = r.GetString(2), Exchange = r.GetString(3), Sector = r.GetString(4),
            Industry = Text(r, 5), Website = Text(r, 6), Employees = r.IsDBNull(7) ? null : r.GetInt32(7), MarketCap = Money(r, 8),
            Description = Text(r, 9), Currency = r.GetString(10), PayCurrency = Text(r, 11), FiscalYearEnd = Text(r, 12), LogoUrl = Text(r, 13),
            AsOfDate = Text(r, 14) is { } d ? DateOnly.Parse(d, CultureInfo.InvariantCulture) : null, CareersUrl = Text(r, 15)
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
        var workerPay = Query(db, "SELECT company_id, year, median_pay, ceo_pay, ratio, filing_id FROM worker_pay ORDER BY rowid", r => new WorkerPay
        {
            CompanyId = r.GetString(0), Year = r.GetInt32(1), MedianEmployeePay = Money(r, 2)!.Value, CeoPay = Money(r, 3)!.Value,
            Ratio = Money(r, 4)!.Value, SourceFiling = Filing(r, 5)
        });
        var jobSalaries = Query(db, """
            SELECT company_id, title, occupation, city, state, latitude, longitude, filings, low, median, high, min, max, source, url FROM job_salaries ORDER BY rowid
            """, r => new JobSalary
        {
            CompanyId = r.GetString(0), Title = r.GetString(1), Occupation = Text(r, 2), City = Text(r, 3), State = Text(r, 4),
            Point = r.IsDBNull(5) || r.IsDBNull(6) ? null : new GeoPoint(r.GetDouble(5), r.GetDouble(6)),
            Filings = r.GetInt32(7), Low = Money(r, 8)!.Value, Median = Money(r, 9)!.Value, High = Money(r, 10)!.Value,
            Min = Money(r, 11)!.Value, Max = Money(r, 12)!.Value, Source = r.GetString(13), Url = Text(r, 14)
        });
        var extras = Query(db, "SELECT key, value FROM extras", r => (Key: r.GetString(0), Value: r.GetString(1)))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var salarySources = jobSalaries.Select(j => j.Source).Distinct().Select(kind => SalarySource(extras, kind)).OfType<JobSalarySource>().ToList();
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

        return new CompanyData(companies, locations, financials, pay, people, metadata, appointments, workerPay, jobSalaries, salarySources);
    }

    /// <summary>A source's date range from the extras table (layout 3 stored the visa filings' without the kind).</summary>
    private static JobSalarySource? SalarySource(Dictionary<string, string> extras, string kind)
    {
        string? Get(string key) => extras.GetValueOrDefault($"job_salaries.{kind}.{key}") ??
                                   (kind == JobSalary.VisaFilings ? extras.GetValueOrDefault($"job_salaries.{key}") : null);
        return DateOnly.TryParse(Get("from"), CultureInfo.InvariantCulture, out var from) && DateOnly.TryParse(Get("to"), CultureInfo.InvariantCulture, out var to)
            ? new JobSalarySource(from, to, Get("source") ?? "", kind)
            : null;
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
                                   currency, pay_currency, fiscal_year_end, logo_url, as_of_date, careers_url)
            VALUES ($0, $market, $1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15)
            """, market, 16))
            foreach (var c in data.Companies)
                Run(cmd, c.CompanyId, c.Name, c.Ticker, c.Exchange, c.Sector, c.Industry, c.Website, c.Employees, c.MarketCap, c.Description,
                    c.Currency, c.PayCurrency, c.FiscalYearEnd, c.LogoUrl, c.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.CareersUrl);

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

        using (var cmd = Command(db, """
            INSERT INTO worker_pay (company_id, market, year, median_pay, ceo_pay, ratio, filing_id) VALUES ($0, $market, $1, $2, $3, $4, $5)
            """, market, 6))
            foreach (var w in data.WorkerPays)
                Run(cmd, w.CompanyId, w.Year, w.MedianEmployeePay, w.CeoPay, w.Ratio, FilingId(w.SourceFiling));
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
