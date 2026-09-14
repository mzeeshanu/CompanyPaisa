using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Importer.Uk;

/// <summary>One index member from the curated list. <see cref="Lei"/> can be filled in by hand to fix a bad match.</summary>
public sealed record UkConstituent(string Ticker, string Name, string Sector, string? Lei);

/// <summary>A UK company that files ESEF annual reports on filings.xbrl.org.</summary>
public sealed record UkEntity(string Lei, string Name, List<UkFiling> Filings);

public sealed record UkFiling(DateOnly PeriodEnd, string JsonUrl, string ReportUrl, int Errors);

public sealed record UkAddress(string Street, string City, string PostalCode, string Country);

/// <summary>FTSE 100 + 250 members, read from Wikipedia's constituent tables and kept as a reviewable CSV.</summary>
public static partial class UkConstituents
{
    public static List<UkConstituent> Read(string path)
    {
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => Csv.Split(l))
            .Where(f => f.Count >= 3)
            .Select(f => new UkConstituent(f[0].Trim().ToUpperInvariant(), f[1].Trim(), f[2].Trim(), f.Count > 3 && f[3].Trim().Length > 0 ? f[3].Trim() : null))
            .ToList();
    }

    public static void Write(string path, IEnumerable<UkConstituent> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, rows.Select(r => string.Join(',', Csv.Quote(r.Ticker), Csv.Quote(r.Name), Csv.Quote(r.Sector), Csv.Quote(r.Lei ?? "")))
            .Prepend("ticker,name,sector,lei"), new UTF8Encoding(false));
    }

    /// <summary>Parses the wikitable whose header starts "Company | Ticker | … sector".</summary>
    public static async Task<List<UkConstituent>> FetchAsync(ISecClient client, IEnumerable<string> pages, CancellationToken ct)
    {
        var result = new List<UkConstituent>();
        foreach (var page in pages)
        {
            var json = await client.GetStringAsync($"https://en.wikipedia.org/w/api.php?action=parse&page={page}&prop=text&format=json&formatversion=2", CachePolicy.Index, ct)
                       ?? throw new InvalidOperationException($"Couldn't read Wikipedia page {page}.");
            using var doc = JsonDocument.Parse(json);
            var html = doc.RootElement.GetProperty("parse").GetProperty("text").GetString() ?? "";
            foreach (Match table in WikiTable().Matches(html))
            {
                var rows = Row().Matches(table.Value).Select(r => Cell().Matches(r.Value).Select(c => Clean(c.Groups[1].Value)).ToList()).ToList();
                if (rows.Count < 2 || rows[0].Count < 3 || !rows[0][0].StartsWith("Company", StringComparison.OrdinalIgnoreCase) ||
                    !rows[0][1].StartsWith("Ticker", StringComparison.OrdinalIgnoreCase)) continue;
                result.AddRange(rows.Skip(1).Where(r => r.Count >= 3 && r[1].Length > 0)
                    .Select(r => new UkConstituent(r[1].TrimEnd('.').ToUpperInvariant(), r[0], r[2], null)));
            }
        }
        return result.DistinctBy(r => r.Ticker).ToList();
    }

    private static string Clean(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"<sup[\s\S]*?</sup>", ""), "<[^>]+>", "")).Trim();

    [GeneratedRegex(@"<table class=""wikitable[\s\S]*?</table>")] private static partial Regex WikiTable();
    [GeneratedRegex(@"<tr[^>]*>[\s\S]*?</tr>")] private static partial Regex Row();
    [GeneratedRegex(@"<t[hd][^>]*>([\s\S]*?)</t[hd]>")] private static partial Regex Cell();
}

/// <summary>Every UK filer on filings.xbrl.org with its LEI, name and annual reports.</summary>
public static class UkFilingsIndex
{
    public static async Task<List<UkEntity>> LoadAsync(ISecClient client, string api, CancellationToken ct)
    {
        var entities = new Dictionary<string, UkEntity>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>();   // JSON:API entity id → LEI
        for (var page = 1; ; page++)
        {
            var url = $"{api}/api/filings?filter%5Bcountry%5D=GB&include=entity&page%5Bsize%5D=200&page%5Bnumber%5D={page}";
            var json = await client.GetStringAsync(url, CachePolicy.Index, ct) ?? throw new InvalidOperationException($"filings.xbrl.org page {page} failed; re-run.");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("included", out var included))
                foreach (var e in included.EnumerateArray())
                {
                    var lei = e.GetProperty("attributes").GetProperty("identifier").GetString()!;
                    names[e.GetProperty("id").GetString()!] = lei;
                    entities.TryAdd(lei, new UkEntity(lei, e.GetProperty("attributes").GetProperty("name").GetString() ?? lei, []));
                }
            var data = root.GetProperty("data").EnumerateArray().ToList();
            foreach (var f in data)
            {
                var a = f.GetProperty("attributes");
                var entityId = f.GetProperty("relationships").GetProperty("entity").TryGetProperty("data", out var ed) && ed.ValueKind == JsonValueKind.Object
                    ? ed.GetProperty("id").GetString() : null;
                var lei = entityId is not null && names.TryGetValue(entityId, out var l) ? l
                    : a.GetProperty("fxo_id").GetString()!.Split('-')[0];
                if (!entities.TryGetValue(lei, out var entity)) entities[lei] = entity = new UkEntity(lei, lei, []);
                if (!DateOnly.TryParse(a.GetProperty("period_end").GetString(), CultureInfo.InvariantCulture, out var end)) continue;
                entity.Filings.Add(new UkFiling(end, api + a.GetProperty("json_url").GetString(), api + a.GetProperty("report_url").GetString(),
                    a.TryGetProperty("error_count", out var err) && err.ValueKind == JsonValueKind.Number ? err.GetInt32() : 0));
            }
            if (data.Count == 0 || !root.GetProperty("links").TryGetProperty("next", out var next) || next.ValueKind == JsonValueKind.Null) break;
        }
        foreach (var e in entities.Values)
        {
            // A period can be filed twice (a correction); keep the one with fewer validation errors.
            var best = e.Filings.GroupBy(f => f.PeriodEnd).Select(g => g.OrderBy(f => f.Errors).First()).OrderByDescending(f => f.PeriodEnd).ToList();
            e.Filings.Clear();
            e.Filings.AddRange(best);
        }
        return entities.Values.ToList();
    }
}

/// <summary>Headquarters addresses from the global LEI registry.</summary>
public static class Gleif
{
    public static async Task<(UkAddress? Headquarters, UkAddress? Legal)> GetAddressesAsync(ISecClient client, string api, string lei, CancellationToken ct)
    {
        var json = await client.GetStringAsync($"{api}/lei-records/{lei}", CachePolicy.Index, ct);
        if (json is null) return (null, null);
        using var doc = JsonDocument.Parse(json);
        var entity = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("entity");
        return (Address(entity, "headquartersAddress"), Address(entity, "legalAddress"));
    }

    private static UkAddress? Address(JsonElement entity, string name)
    {
        if (!entity.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Object) return null;
        var lines = a.TryGetProperty("addressLines", out var al) ? al.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0) : [];
        return new UkAddress(string.Join(", ", lines), a.GetProperty("city").GetString() ?? "", a.GetProperty("postalCode").GetString() ?? "",
            a.GetProperty("country").GetString() ?? "");
    }
}

/// <summary>UK postcode districts (GeoNames): district → centre and a place name; writes the API's lookup table.</summary>
public sealed class UkPostcodes
{
    private readonly Dictionary<string, (string Place, GeoPoint Point)> _districts = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _districts.Count;

    public static async Task<UkPostcodes> LoadAsync(ISecClient client, string url, CancellationToken ct)
    {
        var zip = await client.GetBytesAsync(url, CachePolicy.Immutable, ct) ?? throw new InvalidOperationException($"Couldn't download {url}.");
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var reader = new StreamReader(archive.Entries.First(e => e.Name.Equals("GB.txt", StringComparison.OrdinalIgnoreCase)).Open(), Encoding.UTF8);
        var rows = new Dictionary<string, List<(string Place, string County, double Lat, double Lng)>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 11 || !double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            if (!rows.TryGetValue(f[1], out var list)) rows[f[1]] = list = [];
            list.Add((f[2], f[5], lat, lng));
        }
        var result = new UkPostcodes();
        foreach (var (district, list) in rows)
        {
            // GeoNames lists several neighbourhoods per district; a London district is simply "London".
            var place = list.Any(x => x.County == "Greater London") ? "London" : list[0].Place;
            result._districts[district] = (place, new GeoPoint(list.Average(x => x.Lat), list.Average(x => x.Lng)));
        }
        return result;
    }

    /// <summary>"AL7 1GA" → AL7; "EC2A 1NQ" → EC2A, else EC2 (the table mixes fine and coarse districts).</summary>
    public (string District, string Place, GeoPoint Point)? Locate(string postcode)
    {
        var pc = Regex.Replace(postcode.ToUpperInvariant(), @"\s+", " ").Trim();
        var district = pc.Contains(' ') ? pc[..pc.IndexOf(' ')] : pc.Length > 3 ? pc[..^3] : pc;
        if (_districts.TryGetValue(district, out var d)) return (district, d.Place, d.Point);
        if (district.Length > 2 && char.IsLetter(district[^1]) && _districts.TryGetValue(district[..^1], out d)) return (district[..^1], d.Place, d.Point);
        return null;
    }

    public async Task<int> WriteTableAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = _districts.OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.Key},{Csv.Quote(d.Value.Place)},UK,{d.Value.Point.Latitude:F5},{d.Value.Point.Longitude:F5}"))
            .Prepend("zip,city,state,latitude,longitude").ToList();
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(false), ct);
        return lines.Count - 1;
    }
}

internal static class Csv
{
    public static string Quote(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    public static List<string> Split(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
