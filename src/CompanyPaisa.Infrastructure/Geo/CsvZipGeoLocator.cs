using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Infrastructure.Geo;

/// <summary>
/// Resolves "84043", "84043-1234", "Lehi, UT", "Lehi UT" or "Lehi" from a local CSV
/// (columns: zip,city,state,latitude,longitude). Loaded once, on first use.
/// </summary>
public sealed partial class CsvZipGeoLocator(
    IOptions<GeoOptions> options,
    IFilePathResolver paths,
    ILogger<CsvZipGeoLocator> logger) : IGeoLocator
{
    private readonly Lazy<ZipIndex> _index = new(() => ZipIndex.Load(paths.Resolve(options.Value.ZipTablePath), logger));

    public Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        var index = _index.Value;

        var zip = ZipPattern().Match(q);
        if (zip.Success)
            return Task.FromResult(index.ByZip.TryGetValue(zip.Groups[1].Value, out var z) ? z with { Query = q } : null);

        var cityState = CityStatePattern().Match(q);
        if (cityState.Success)
        {
            var key = ZipIndex.CityKey(cityState.Groups["city"].Value, cityState.Groups["state"].Value);
            if (index.ByCityState.TryGetValue(key, out var cs)) return Task.FromResult<GeoLookupResult?>(cs with { Query = q });
        }

        // City alone: only answer when the name is unambiguous.
        return Task.FromResult(index.ByCity.TryGetValue(q.ToUpperInvariant(), out var list) && list.Count == 1
            ? list[0] with { Query = q }
            : null);
    }

    [GeneratedRegex(@"^(\d{5})(?:-\d{4})?$")]
    private static partial Regex ZipPattern();

    [GeneratedRegex(@"^(?<city>[A-Za-z .'\-]+?)[,\s]+(?<state>[A-Za-z]{2})$")]
    private static partial Regex CityStatePattern();

    private sealed class ZipIndex
    {
        public Dictionary<string, GeoLookupResult> ByZip { get; } = new();
        public Dictionary<string, GeoLookupResult> ByCityState { get; } = new();
        public Dictionary<string, List<GeoLookupResult>> ByCity { get; } = new();

        public static string CityKey(string city, string state) => $"{city.Trim().ToUpperInvariant()}|{state.Trim().ToUpperInvariant()}";

        public static ZipIndex Load(string path, ILogger logger)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"ZIP table not found at '{path}'. Check Geo:ZipTablePath in appsettings.", path);

            var index = new ZipIndex();
            var cityPoints = new Dictionary<string, List<(GeoPoint Point, string City, string State)>>();
            var lines = File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Col(string name) => header.IndexOf(name) is var i and >= 0 ? i
                : throw new FormatException($"ZIP table '{path}' is missing the '{name}' column.");
            int cZip = Col("zip"), cCity = Col("city"), cState = Col("state"), cLat = Col("latitude"), cLng = Col("longitude");

            foreach (var line in lines.Skip(1))
            {
                var f = SplitCsv(line);
                if (!double.TryParse(f[cLat], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(f[cLng], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;

                var point = new GeoPoint(lat, lng);
                var zip = f[cZip].Trim().PadLeft(5, '0');
                index.ByZip[zip] = new GeoLookupResult(zip, f[cCity].Trim(), f[cState].Trim().ToUpperInvariant(), zip, point);

                var key = CityKey(f[cCity], f[cState]);
                if (!cityPoints.TryGetValue(key, out var pts)) cityPoints[key] = pts = [];
                pts.Add((point, f[cCity].Trim(), f[cState].Trim().ToUpperInvariant()));
            }

            // A city is the average of its ZIP centroids.
            foreach (var (key, pts) in cityPoints)
            {
                var p = new GeoPoint(pts.Average(x => x.Point.Latitude), pts.Average(x => x.Point.Longitude));
                var result = new GeoLookupResult(key, pts[0].City, pts[0].State, null, p);
                index.ByCityState[key] = result;
                var cityOnly = pts[0].City.ToUpperInvariant();
                if (!index.ByCity.TryGetValue(cityOnly, out var list)) index.ByCity[cityOnly] = list = [];
                list.Add(result);
            }

            logger.LogInformation("Loaded {Zips} ZIP codes and {Cities} cities from {Path}", index.ByZip.Count, index.ByCityState.Count, path);
            return index;
        }

        /// <summary>Minimal CSV splitter that understands double-quoted fields.</summary>
        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(ch);
            }
            fields.Add(current.ToString());
            return fields;
        }
    }
}
