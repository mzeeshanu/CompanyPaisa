using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CompanyPaisa.Importer.Postings;

/// <summary>A job ad as remembered: its board, the company, and when it was first and last seen open.</summary>
public sealed record SeenPosting(string System, string Board, string CompanyId, Posting Posting, DateOnly FirstSeen, DateOnly LastSeen);

/// <summary>
/// Every job ad seen, kept between runs on this computer (data/cache/postings/postings.db, not published): ads are
/// open for weeks, so monthly runs build up a year of them, and a Workday or SmartRecruiters ad's text is read once.
/// </summary>
public sealed class PostingStore : IDisposable
{
    private readonly SqliteConnection _db;

    public PostingStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        Execute("""
            CREATE TABLE IF NOT EXISTS postings (
                system TEXT NOT NULL, board TEXT NOT NULL, id TEXT NOT NULL, company_id TEXT NOT NULL, title TEXT NOT NULL,
                location TEXT NOT NULL, url TEXT NOT NULL, pay_min REAL, pay_max REAL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
                PRIMARY KEY (system, board, id));
            """);
    }

    /// <summary>The ads already seen on one board, by id.</summary>
    public Dictionary<string, Posting> Known(JobBoard board)
    {
        var known = new Dictionary<string, Posting>(StringComparer.Ordinal);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, title, location, url, pay_min, pay_max FROM postings WHERE system = $s AND board = $b";
        cmd.Parameters.AddWithValue("$s", board.System);
        cmd.Parameters.AddWithValue("$b", board.Board);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            known[r.GetString(0)] = new Posting(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) || r.IsDBNull(5) ? null : new PayRange((decimal)r.GetDouble(4), (decimal)r.GetDouble(5)));
        return known;
    }

    /// <summary>Records the ads open on a board today (new ones added, the rest marked as still open).</summary>
    public void Save(JobBoard board, string companyId, IEnumerable<Posting> open, DateOnly today)
    {
        using var tx = _db.BeginTransaction();
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO postings (system, board, id, company_id, title, location, url, pay_min, pay_max, first_seen, last_seen)
            VALUES ($s, $b, $id, $c, $t, $l, $u, $min, $max, $d, $d)
            ON CONFLICT (system, board, id) DO UPDATE SET last_seen = $d, company_id = $c, title = $t, location = $l, url = $u,
                pay_min = COALESCE($min, pay_min), pay_max = COALESCE($max, pay_max)
            """;
        foreach (var name in new[] { "$s", "$b", "$id", "$c", "$t", "$l", "$u", "$min", "$max", "$d" }) cmd.Parameters.Add(new SqliteParameter(name, null));
        foreach (var p in open)
        {
            cmd.Parameters["$s"].Value = board.System;
            cmd.Parameters["$b"].Value = board.Board;
            cmd.Parameters["$id"].Value = p.Id;
            cmd.Parameters["$c"].Value = companyId;
            cmd.Parameters["$t"].Value = p.Title;
            cmd.Parameters["$l"].Value = p.Location;
            cmd.Parameters["$u"].Value = p.Url;
            cmd.Parameters["$min"].Value = p.Pay is { } pay ? (double)pay.Min : DBNull.Value;
            cmd.Parameters["$max"].Value = p.Pay is { } pay2 ? (double)pay2.Max : DBNull.Value;
            cmd.Parameters["$d"].Value = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Ads with a stated pay range seen open on or after <paramref name="since"/>.</summary>
    public List<SeenPosting> WithPay(DateOnly since)
    {
        var list = new List<SeenPosting>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT system, board, company_id, id, title, location, url, pay_min, pay_max, first_seen, last_seen FROM postings
            WHERE pay_min IS NOT NULL AND pay_max IS NOT NULL AND last_seen >= $since
            """;
        cmd.Parameters.AddWithValue("$since", since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new SeenPosting(r.GetString(0), r.GetString(1), r.GetString(2),
                new Posting(r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), new PayRange((decimal)r.GetDouble(7), (decimal)r.GetDouble(8))),
                DateOnly.Parse(r.GetString(9), CultureInfo.InvariantCulture), DateOnly.Parse(r.GetString(10), CultureInfo.InvariantCulture)));
        return list;
    }

    /// <summary>How many ads (and with pay) have been seen in all.</summary>
    public (int All, int WithPay) Counts()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT count(*), count(pay_min) FROM postings";
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    private void Execute(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
