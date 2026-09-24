using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
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
    ILogger<CsvZipGeoLocator> logger) : IGeoLocator, IReverseGeoLocator
{
    private readonly Lazy<ZipIndex> _index = new(() => ZipIndex.Load(
        options.Value.AdditionalTablePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(paths.Resolve).Prepend(paths.Resolve(options.Value.ZipTablePath)).ToList(),
        logger));

    public Task<GeoLookupResult?> LookupAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        var index = _index.Value;

        // Europe, Australia, New Zealand, Pakistan: the website sends the country with the code ("FR-75008", "NL 1012 AB",
        // "AU-2000", "PK-74000"), because a bare 5-digit French, Italian, Spanish or Pakistani postcode looks like a US ZIP.
        var eu = EuropeanPostcodePattern().Match(q.ToUpperInvariant());
        if (eu.Success)
        {
            var country = eu.Groups["country"].Value;
            var digits = eu.Groups["digits"].Value;
            var code = country is "NL" or "AU" or "NZ" ? digits[..Math.Min(4, digits.Length)] : digits.PadLeft(5, '0');
            return Task.FromResult(index.ByZip.TryGetValue($"{country}:{code}", out var e)
                ? e with { Query = q, PostalCode = country == "NL" && eu.Groups["letters"].Success ? $"{code} {eu.Groups["letters"].Value}" : code }
                : null);
        }

        var zip = ZipPattern().Match(q);
        if (zip.Success)
            return Task.FromResult(index.ByZip.TryGetValue(zip.Groups[1].Value, out var z) ? z with { Query = q } : null);

        // Canada: a full postal code ("M5J 2J2") is unambiguous; its first three characters (the FSA) locate it.
        var ca = CanadaPostalPattern().Match(q.ToUpperInvariant());
        if (ca.Success && ca.Groups["ldu"].Success && index.ByZip.TryGetValue(CanadaKey(ca.Groups["fsa"].Value), out var full))
            return Task.FromResult<GeoLookupResult?>(full with { Query = q, PostalCode = $"{ca.Groups["fsa"].Value} {ca.Groups["ldu"].Value}" });

        // UK: "SW1A 1AA", "sw1a1aa" or just the district "SW1A" — all resolve to the district's centre.
        // The district table mixes fine districts ("SW1A") with coarser ones ("EC2"), so "EC2A" falls back to "EC2".
        // A bare "M5J" could be either country: an exact UK district wins, then a Canadian FSA, then the coarser UK district.
        var uk = UkPostcodePattern().Match(q.ToUpperInvariant());
        if (uk.Success)
        {
            var district = uk.Groups["district"].Value;
            var postal = uk.Groups["inward"].Success ? $"{district} {uk.Groups["inward"].Value}" : district;
            if (index.ByZip.TryGetValue(district, out var d))
                return Task.FromResult<GeoLookupResult?>(d with { Query = q, PostalCode = postal });
            if (ca.Success && index.ByZip.TryGetValue(CanadaKey(ca.Groups["fsa"].Value), out var fsa))
                return Task.FromResult<GeoLookupResult?>(fsa with { Query = q });
            if (char.IsLetter(district[^1]) && index.ByZip.TryGetValue(district[..^1], out d))
                return Task.FromResult<GeoLookupResult?>(d with { Query = q, PostalCode = postal });
        }
        else if (ca.Success && index.ByZip.TryGetValue(CanadaKey(ca.Groups["fsa"].Value), out var fsa))
            return Task.FromResult<GeoLookupResult?>(fsa with { Query = q });

        var cityState = CityStatePattern().Match(q);
        if (cityState.Success)
        {
            var key = ZipIndex.CityKey(cityState.Groups["city"].Value, cityState.Groups["state"].Value);
            if (index.ByCityState.TryGetValue(key, out var cs)) return Task.FromResult<GeoLookupResult?>(cs with { Query = q });
        }

        // City alone: the biggest place with that name ("Dallas" → Dallas, TX; "Portland" → Portland, OR).
        if (index.ByCity.TryGetValue(q.ToUpperInvariant(), out var list)) return Task.FromResult<GeoLookupResult?>(list[0] with { Query = q });

        // A city followed by its country: "Dallas, USA", "Toronto Canada" — look up the city part.
        return Regions.SplitCountry(q) is var (city, _) ? LookupAsync(city, ct).ContinueWith(t => t.Result is { } hit ? hit with { Query = q } : null, ct) : Task.FromResult<GeoLookupResult?>(null);
    }

    public Task<GeoLookupResult?> NearestCityAsync(GeoPoint point, double maxMiles, CancellationToken ct = default)
    {
        // A flat-earth comparison is plenty to rank candidates; the winner is then checked with the real distance.
        var cos = Math.Cos(point.Latitude * Math.PI / 180);
        GeoLookupResult? best = null;
        var bestScore = double.MaxValue;
        foreach (var city in _index.Value.ByCityState.Values)
        {
            var dLat = city.Point.Latitude - point.Latitude;
            var dLng = (city.Point.Longitude - point.Longitude) * cos;
            var score = dLat * dLat + dLng * dLng;
            if (score < bestScore) { bestScore = score; best = city; }
        }
        return Task.FromResult(best is not null && new HaversineDistanceCalculator().DistanceMiles(point, best.Point) <= maxMiles ? best : null);
    }

    [GeneratedRegex(@"^(\d{5})(?:-\d{4})?$")]
    private static partial Regex ZipPattern();

    [GeneratedRegex(@"^(?<city>[A-Za-z .'\-]+?)[,\s]+(?<state>[A-Za-z]{2})$")]
    private static partial Regex CityStatePattern();

    /// <summary>
    /// "FR-75008", "IT 00184", "ES-08002", "NL-1012 AB", "AU-2000", "NZ-1010", "PK-74000": country, then the code
    /// (Dutch codes may carry two letters; Dutch, Australian and New Zealand codes are 4 digits).
    /// </summary>
    [GeneratedRegex(@"^(?<country>FR|NL|IT|ES|AU|NZ|PK)[\s\-:]+(?<digits>\d{4,5})(?:\s*(?<letters>[A-Z]{2}))?$")]
    private static partial Regex EuropeanPostcodePattern();

    /// <summary>Canadian postal code: forward sortation area ("M5J") and an optional local delivery unit ("2J2").</summary>
    [GeneratedRegex(@"^(?<fsa>[ABCEGHJ-NPRSTVXY]\d[A-Z])(?:\s*(?<ldu>\d[A-Z]\d))?$")]
    private static partial Regex CanadaPostalPattern();

    /// <summary>Canadian areas are stored under "CA:M5J" so they never collide with a UK district of the same name.</summary>
    private static string CanadaKey(string fsa) => "CA:" + fsa;

    private static readonly HashSet<string> Provinces = ["AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"];

    /// <summary>UK postcode: outward code (district) and an optional inward code.</summary>
    [GeneratedRegex(@"^(?<district>[A-Z]{1,2}\d[A-Z\d]?)(?:\s*(?<inward>\d[A-Z]{2}))?$")]
    private static partial Regex UkPostcodePattern();

    private sealed class ZipIndex
    {
        public Dictionary<string, GeoLookupResult> ByZip { get; } = new();
        public Dictionary<string, GeoLookupResult> ByCityState { get; } = new();
        public Dictionary<string, List<GeoLookupResult>> ByCity { get; } = new();

        public static string CityKey(string city, string state) => $"{city.Trim().ToUpperInvariant()}|{state.Trim().ToUpperInvariant()}";

        public static ZipIndex Load(IReadOnlyList<string> paths, ILogger logger)
        {
            if (!File.Exists(paths[0]))
                throw new FileNotFoundException($"ZIP table not found at '{paths[0]}'. Check Geo:ZipTablePath in appsettings.", paths[0]);

            var index = new ZipIndex();
            var cityPoints = new Dictionary<string, List<(GeoPoint Point, string City, string State)>>();
            foreach (var path in paths)
            {
                if (!File.Exists(path)) { logger.LogWarning("Additional postcode table {Path} not found; skipping it", path); continue; }
                LoadTable(path, index, cityPoints);
            }

            // A city is the average of its ZIP centroids. Cities sharing a name are listed biggest first (most ZIP codes),
            // so "Dallas" alone is Dallas, Texas rather than Dallas, Georgia.
            foreach (var (key, pts) in cityPoints.OrderByDescending(c => c.Value.Count))
            {
                var p = new GeoPoint(pts.Average(x => x.Point.Latitude), pts.Average(x => x.Point.Longitude));
                var result = new GeoLookupResult(key, pts[0].City, pts[0].State, null, p);
                index.ByCityState[key] = result;
                var cityOnly = pts[0].City.ToUpperInvariant();
                if (!index.ByCity.TryGetValue(cityOnly, out var list)) index.ByCity[cityOnly] = list = [];
                list.Add(result);
            }

            logger.LogInformation("Loaded {Zips} ZIP codes / postcode districts and {Cities} cities from {Count} table(s)", index.ByZip.Count, index.ByCityState.Count, paths.Count);
            return index;
        }

        private static void LoadTable(string path, ZipIndex index, Dictionary<string, List<(GeoPoint Point, string City, string State)>> cityPoints)
        {
            var lines = File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Col(string name) => header.IndexOf(name) is var i and >= 0 ? i
                : throw new FormatException($"ZIP table '{path}' is missing the '{name}' column.");
            int cZip = Col("zip"), cCity = Col("city"), cState = Col("state"), cLat = Col("latitude"), cLng = Col("longitude");
            // Optional "country" column (European tables): rows are keyed "FR:75008" so they never collide with US ZIPs.
            var cCountry = header.IndexOf("country");

            foreach (var line in lines.Skip(1))
            {
                var f = SplitCsv(line);
                if (!double.TryParse(f[cLat], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(f[cLng], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;

                var point = new GeoPoint(lat, lng);
                // US ZIPs lose leading zeros in spreadsheets; UK districts ("SW1A") are kept as they are.
                var raw = f[cZip].Trim().ToUpperInvariant();
                var zip = raw.All(char.IsDigit) ? raw.PadLeft(5, '0') : raw;
                var state = f[cState].Trim().ToUpperInvariant();
                var country = cCountry >= 0 ? f[cCountry].Trim().ToUpperInvariant() : "";
                var zipKey = country.Length > 0 ? $"{country}:{raw}" : Provinces.Contains(state) ? CanadaKey(zip) : zip;
                var shown = country.Length > 0 ? raw : zip;   // Dutch "1012" must not become "01012"
                index.ByZip[zipKey] = new GeoLookupResult(shown, f[cCity].Trim(), state, shown, point);

                var key = CityKey(f[cCity], f[cState]);
                if (!cityPoints.TryGetValue(key, out var pts)) cityPoints[key] = pts = [];
                pts.Add((point, f[cCity].Trim(), f[cState].Trim().ToUpperInvariant()));
            }
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
