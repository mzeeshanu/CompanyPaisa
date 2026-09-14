using System.Globalization;
using System.IO.Compression;
using System.Text;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Geo;

/// <summary>ZIP code → coordinates (Census Gazetteer ZCTA centroids), plus the region test.</summary>
public interface IZipGeocoder
{
    Task LoadAsync(CancellationToken ct);
    GeoPoint? Locate(string zip);
    /// <summary>ZIP centroid, or — for PO-box / single-business ZIPs the Census doesn't map — another ZIP in the same city.</summary>
    GeoPoint? Locate(string zip, string city, string? state = null);
    /// <summary>Remember a city name for a ZIP (from SEC addresses) so the generated ZIP table can name it.</summary>
    void LearnCityName(string zip, string city, string state);
    /// <summary>Two-letter state for a ZIP, if known.</summary>
    string? StateOf(string zip);
    /// <summary>Writes zip,city,state,latitude,longitude for the configured prefixes (the API's ZIP lookup table).</summary>
    Task<int> WriteZipTableAsync(CancellationToken ct);
    /// <summary>The metro (and anchor within it) the point falls in, or null if it's outside every covered metro.</summary>
    /// <summary>The metro whose circle contains the point, else a whole-state region covering <paramref name="state"/>.</summary>
    (string Region, string Anchor)? RegionFor(GeoPoint point, string? state);
}

public sealed class ZipGeocoder(ISecClient client, IOptions<ImporterOptions> options, RepoPaths paths, ILogger<ZipGeocoder> logger) : IZipGeocoder
{
    private readonly Dictionary<string, GeoPoint> _centroids = new();
    private readonly Dictionary<string, (string City, string State)> _names = new();
    private readonly HaversineDistanceCalculator _distance = new();

    public async Task LoadAsync(CancellationToken ct)
    {
        var o = options.Value.Geo;
        var zip = await client.GetBytesAsync(o.GazetteerUrl, CachePolicy.Immutable, ct)
                  ?? throw new InvalidOperationException($"Couldn't download the ZIP gazetteer from {o.GazetteerUrl}.");
        using var archive = new ZipArchive(new MemoryStream(zip));
        var entry = archive.Entries.First(e => e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);

        // Older Gazetteer editions are tab-separated; 2025 uses '|'.
        var headerLine = (await reader.ReadLineAsync(ct))!;
        var sep = headerLine.Contains('|') ? '|' : '\t';
        var header = headerLine.Split(sep).Select(h => h.Trim()).ToList();
        int iZip = header.IndexOf("GEOID"), iLat = header.IndexOf("INTPTLAT"), iLng = header.IndexOf("INTPTLONG");
        if (iZip < 0 || iLat < 0 || iLng < 0) throw new FormatException($"Unexpected Gazetteer columns: {headerLine}");
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var f = line.Split(sep);
            if (f.Length <= Math.Max(iLat, iLng)) continue;
            if (double.TryParse(f[iLat].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(f[iLng].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                _centroids[f[iZip].Trim()] = new GeoPoint(lat, lng);
        }

        // ZIP → city/state names (and coordinates for PO-box ZIPs the Census doesn't map) from GeoNames.
        if (!string.IsNullOrWhiteSpace(o.PlaceNamesUrl) && await client.GetBytesAsync(o.PlaceNamesUrl, CachePolicy.Immutable, ct) is { } names)
        {
            using var namesZip = new ZipArchive(new MemoryStream(names));
            var txt = namesZip.Entries.First(e => e.Name.Equals("US.txt", StringComparison.OrdinalIgnoreCase));
            using var nr = new StreamReader(txt.Open(), Encoding.UTF8);
            var extra = 0;
            // country, postal code, place name, state name, state code, county, county code, …, latitude, longitude, accuracy
            while (await nr.ReadLineAsync(ct) is { } line)
            {
                var f = line.Split('\t');
                if (f.Length < 11 || f[1].Length != 5) continue;
                _names.TryAdd(f[1], (f[2].Trim(), f[4].Trim().ToUpperInvariant()));
                if (!_centroids.ContainsKey(f[1]) &&
                    double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
                    double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo))
                { _centroids[f[1]] = new GeoPoint(la, lo); extra++; }
            }
            logger.LogInformation("Loaded {Count} ZIP names from GeoNames ({Extra} ZIPs the Census doesn't map, e.g. PO boxes)", _names.Count, extra);
        }

        // Seed names from the existing hand-made table.
        var seed = paths.Resolve(o.ZipNamesSeed);
        if (File.Exists(seed))
            foreach (var line in File.ReadLines(seed).Skip(1))
            {
                var f = line.Split(',');
                if (f.Length >= 3) _names.TryAdd(f[0].Trim(), (f[1].Trim(), f[2].Trim()));
            }
        logger.LogInformation("Loaded {Count} ZIP centroids from the Census Gazetteer", _centroids.Count);
    }

    public GeoPoint? Locate(string zip)
    {
        var z = (zip ?? "").Trim();
        if (z.Length >= 5) z = z[..5];
        return _centroids.TryGetValue(z, out var p) ? p : null;
    }

    public GeoPoint? Locate(string zip, string city, string? state = null)
    {
        if (Locate(zip) is { } exact) return exact;
        var name = Text.TitleCase(city);
        // Nationwide there are many Springfields: only borrow a ZIP from the same city in the same state.
        var sameCity = _names.Where(kv => string.Equals(kv.Value.City, name, StringComparison.OrdinalIgnoreCase) && _centroids.ContainsKey(kv.Key) &&
                                          (state is null || string.Equals(kv.Value.State, state, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key).Order().FirstOrDefault();
        return sameCity is null ? null : _centroids[sameCity];
    }

    public void LearnCityName(string zip, string city, string state)
    {
        if (zip.Length >= 5 && !string.IsNullOrWhiteSpace(city)) _names.TryAdd(zip[..5], (Text.TitleCase(city), state.ToUpperInvariant()));
    }

    public async Task<int> WriteZipTableAsync(CancellationToken ct)
    {
        var o = options.Value.Geo;
        var path = paths.Resolve(o.ZipTableOutput);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var rows = _centroids
            .Where(kv => o.ZipPrefixes.Count == 0 || o.ZipPrefixes.Any(p => kv.Key.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var (city, state) = _names.TryGetValue(kv.Key, out var n) ? n : ("", "");
                return string.Create(CultureInfo.InvariantCulture, $"{kv.Key},{Csv(city)},{state},{kv.Value.Latitude:F6},{kv.Value.Longitude:F6}");
            })
            .ToList();
        await File.WriteAllLinesAsync(path, ["zip,city,state,latitude,longitude", .. rows], ct);
        return rows.Count;
    }

    public string? StateOf(string zip) =>
        zip is { Length: >= 5 } && _names.TryGetValue(zip[..5], out var n) && n.State.Length == 2 ? n.State : null;

    public (string Region, string Anchor)? RegionFor(GeoPoint point, string? state)
    {
        var regions = options.Value.Regions;
        // Metro circles first, so a company in the Twin Cities stays in "Minneapolis–St. Paul"...
        var metro = regions
            .SelectMany(r => r.Anchors.Select(a => (Region: r.Name, Anchor: a)))
            .Where(x => _distance.DistanceMiles(point, new GeoPoint(x.Anchor.Latitude, x.Anchor.Longitude)) <= x.Anchor.RadiusMiles)
            .Select(x => ((string Region, string Anchor)?)(x.Region, x.Anchor.Name))
            .FirstOrDefault();
        if (metro is not null || string.IsNullOrWhiteSpace(state)) return metro;
        // ...then whole-state regions pick up the rest (Hormel in Austin, MN).
        return regions.Where(r => r.States.Contains(state, StringComparer.OrdinalIgnoreCase))
            .Select(r => ((string Region, string Anchor)?)(r.Name, state.ToUpperInvariant()))
            .FirstOrDefault();
    }

    private static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;
}

public static class Text
{
    private static readonly HashSet<string> KeepUpper = new(StringComparer.OrdinalIgnoreCase)
        { "LLC", "LP", "USA", "US", "UT", "PLC", "NV", "II", "III", "IV", "REIT", "ETF", "SLC", "AI", "HR", "IT",
          "CEO", "CFO", "COO", "CTO", "CIO", "CMO", "CRO", "CHRO", "EVP", "SVP", "VP", "GC" };

    /// <summary>"DOMO, INC." → "Domo, Inc."; strings that already have lower case are left alone.</summary>
    public static string TitleCase(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower)) return s?.Trim() ?? "";
        var words = s.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => KeepUpper.Contains(w.Trim(',', '.', '/')) ? w.ToUpperInvariant() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w));
        return string.Join(' ', words);
    }
}
