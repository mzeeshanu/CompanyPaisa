using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Importer.Eu;

/// <summary>
/// Postcodes for Europe, Australia and New Zealand (GeoNames): each code → a place name and the average of its points.
/// A French code can cover several villages; a Dutch code is the 4-digit area ("1012"), without the letters.
/// </summary>
public sealed partial class EuPostcodes
{
    private readonly Dictionary<string, (string Country, string Code, string Place, GeoPoint Point)> _codes = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _codes.Count;

    public async Task LoadAsync(ISecClient client, string urlFormat, string country, CancellationToken ct)
    {
        var url = string.Format(CultureInfo.InvariantCulture, urlFormat, country);
        var zip = await client.GetBytesAsync(url, CachePolicy.Immutable, ct) ?? throw new InvalidOperationException($"Couldn't download {url}.");
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var reader = new StreamReader(archive.Entries.First(e => e.Name.Equals($"{country}.txt", StringComparison.OrdinalIgnoreCase)).Open(), Encoding.UTF8);
        var rows = new Dictionary<string, List<(string Place, double Lat, double Lng)>>(StringComparer.OrdinalIgnoreCase);
        // country, postal code, place, admin1 name, admin1 code, admin2…, latitude, longitude, accuracy
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 11 || !double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            var code = Normalise(country, f[1]);
            if (code is null) continue;
            if (!rows.TryGetValue(code, out var list)) rows[code] = list = [];
            // Australian rows name a suburb ("Haymarket"); the city ("Sydney") is in the next-level column.
            var name = country.Equals("AU", StringComparison.OrdinalIgnoreCase) && f[5].Trim().Length > 0 ? f[5].Trim() : f[2].Trim();
            list.Add((name, lat, lng));
        }
        foreach (var (code, list) in rows)
        {
            // "Paris 08" and "Paris" share 75008: the plain city name reads better.
            var place = list.Select(x => ArrondissementSuffix().Replace(x.Place, "")).GroupBy(p => p).MaxBy(g => g.Count())!.Key;
            var point = new GeoPoint(list.Average(x => x.Lat), list.Average(x => x.Lng));
            // Australia's second-level names are councils ("Vincent" for central Perth): near a capital, use the capital.
            if (country.Equals("AU", StringComparison.OrdinalIgnoreCase) &&
                AustralianCapitals.FirstOrDefault(c => Distance.DistanceMiles(point, c.Point) <= 10) is { Name: { } capital })
                place = capital;
            _codes[Key(country, code)] = (country, code, place, point);
        }
    }

    private static readonly Core.Services.HaversineDistanceCalculator Distance = new();

    private static readonly (string Name, GeoPoint Point)[] AustralianCapitals =
    [
        ("Sydney", new(-33.8688, 151.2093)), ("Melbourne", new(-37.8136, 144.9631)), ("Brisbane", new(-27.4698, 153.0251)),
        ("Perth", new(-31.9505, 115.8605)), ("Adelaide", new(-34.9285, 138.6007)), ("Hobart", new(-42.8821, 147.3272)),
        ("Canberra", new(-35.2809, 149.1300)), ("Darwin", new(-12.4634, 130.8456))
    ];

    /// <summary>Countries whose postcodes are 4 digits: the Netherlands ("1012 AB" → 1012), Australia, New Zealand.</summary>
    public static readonly HashSet<string> FourDigitCountries = new(StringComparer.OrdinalIgnoreCase) { "NL", "AU", "NZ" };

    /// <summary>"1012 AB" → "1012" in the Netherlands, "2000" in Sydney; other countries keep their 5 digits ("8002" → "08002").</summary>
    public static string? Normalise(string country, string postcode)
    {
        var digits = PostcodeDigits().Match(postcode).Value;   // "75008", "1012 AB" → 1012, "F-75008" → 75008
        if (FourDigitCountries.Contains(country))
            return digits.Length >= 4 ? digits[..4] : null;
        return digits.Length is >= 4 and <= 5 ? digits.PadLeft(5, '0') : null;
    }

    public (string Place, GeoPoint Point)? Locate(string country, string postcode) =>
        Normalise(country, postcode) is { } code && _codes.TryGetValue(Key(country, code), out var c) ? (c.Place, c.Point) : null;

    /// <summary>The country's postcode whose centre is nearest a point (a headquarters known only by its coordinates).</summary>
    public (string Code, string Place, GeoPoint Point)? Nearest(string country, GeoPoint point) =>
        _codes.Values.Where(c => c.Country.Equals(country, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.Code, c.Place, c.Point, Miles: Distance.DistanceMiles(point, c.Point)))
            .Where(c => c.Miles <= 15).OrderBy(c => c.Miles).Select(c => ((string, string, GeoPoint)?)(c.Code, c.Place, c.Point)).FirstOrDefault();

    private static string Key(string country, string code) => $"{country.ToUpperInvariant()}:{code}";

    /// <summary>zip,city,state,country,latitude,longitude — the API keys these rows by country ("FR:75008").</summary>
    public async Task<int> WriteTableAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = _codes.Values.OrderBy(c => c.Country, StringComparer.Ordinal).ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Code},{Quote(c.Place)},{c.Country},{c.Country},{c.Point.Latitude:F5},{c.Point.Longitude:F5}"))
            .Prepend("zip,city,state,country,latitude,longitude").ToList();
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(false), ct);
        return lines.Count - 1;
    }

    private static string Quote(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    [GeneratedRegex(@"\s+\d{1,2}(\s.*)?$")] private static partial Regex ArrondissementSuffix();
    [GeneratedRegex(@"\d{4,5}")] private static partial Regex PostcodeDigits();
}
