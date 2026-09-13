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
    GeoPoint? Locate(string zip, string city);
    /// <summary>Remember a city name for a ZIP (from SEC addresses) so the generated ZIP table can name it.</summary>
    void LearnCityName(string zip, string city, string state);
    /// <summary>Writes zip,city,state,latitude,longitude for the configured prefixes (the API's ZIP lookup table).</summary>
    Task<int> WriteZipTableAsync(CancellationToken ct);
    /// <summary>The configured anchor the point is inside, or null if outside the region.</summary>
    string? RegionAnchorFor(GeoPoint point);
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

    public GeoPoint? Locate(string zip, string city)
    {
        if (Locate(zip) is { } exact) return exact;
        var name = Text.TitleCase(city);
        var sameCity = _names.Where(kv => string.Equals(kv.Value.City, name, StringComparison.OrdinalIgnoreCase) && _centroids.ContainsKey(kv.Key))
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
                var (city, state) = _names.TryGetValue(kv.Key, out var n) ? n : ("", "UT");
                return string.Create(CultureInfo.InvariantCulture, $"{kv.Key},{Csv(city)},{state},{kv.Value.Latitude:F6},{kv.Value.Longitude:F6}");
            })
            .ToList();
        await File.WriteAllLinesAsync(path, ["zip,city,state,latitude,longitude", .. rows], ct);
        return rows.Count;
    }

    public string? RegionAnchorFor(GeoPoint point) =>
        options.Value.Region.Anchors
            .Where(a => _distance.DistanceMiles(point, new GeoPoint(a.Latitude, a.Longitude)) <= a.RadiusMiles)
            .Select(a => a.Name)
            .FirstOrDefault();

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
