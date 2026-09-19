using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Importer.Pk;

/// <summary>
/// Places in Pakistan (GeoNames): towns of 5,000+ people with their positions, and the postcodes. A company's address
/// ("… Shahrah-e-Faisal, Karachi") is placed at the last town it names. Postcode positions in GeoNames are often only the
/// province's centre, so a postcode takes its town's position when its name starts with one ("Lahore Gpo" → Lahore).
/// </summary>
public sealed partial class PkPlaces
{
    private sealed record Town(string Name, string Province, GeoPoint Point, long Population);

    private readonly Dictionary<string, Town> _towns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Place, GeoPoint Point, string? Town)> _postcodes = new(StringComparer.Ordinal);
    private Regex? _townPattern;

    public async Task LoadAsync(ISecClient client, string citiesUrl, string postcodesUrl, CancellationToken ct)
    {
        // geonameid, name, asciiname, alternatenames, latitude, longitude, class, code, country, cc2, admin1, …, population
        foreach (var f in await ReadZipAsync(client, citiesUrl, "cities5000.txt", ct))
        {
            if (f.Length < 15 || f[8] != "PK") continue;
            if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            var population = long.TryParse(f[14], out var p) ? p : 0;
            var town = new Town(f[2].Trim(), Province(f[10]), new GeoPoint(lat, lng), population);
            // Two towns with one name: the bigger one.
            if (!_towns.TryGetValue(town.Name, out var seen) || seen.Population < population) _towns[town.Name] = town;
        }
        // Longest names first, so "Dera Ghazi Khan" wins over "Khan…" style partial names.
        _townPattern = new Regex(@"\b(" + string.Join("|", _towns.Keys.Where(k => k.Length >= 4).OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // country, postal code, place, admin1 name, admin1 code, …, latitude, longitude, accuracy
        foreach (var f in await ReadZipAsync(client, postcodesUrl, "PK.txt", ct))
        {
            if (f.Length < 11 || !double.TryParse(f[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            var code = f[1].Trim();
            if (!FiveDigits().IsMatch(code) || _postcodes.ContainsKey(code)) continue;
            var place = f[2].Trim();
            var town = _townPattern.Match(place) is { Success: true, Index: 0 } m ? _towns[m.Value] : null;
            if (town is not null) _postcodes[code] = (town.Name, town.Point, town.Name);
            // Accuracy 4 = the post office's own position; 1 = only the province: not usable without a town.
            else if (f.Length > 11 && f[11].Trim() == "4") _postcodes[code] = (GpoSuffix().Replace(place, ""), new GeoPoint(lat, lng), null);
        }
    }

    public int PostcodeCount => _postcodes.Count;

    /// <summary>The town an address names (the last one, since addresses end with the city), its position and a postcode.</summary>
    public (string City, string Province, string PostalCode, GeoPoint Point)? Locate(string address)
    {
        if (_townPattern is null) throw new InvalidOperationException("Load the places first.");
        var text = address.Replace("Pakistan", " ", StringComparison.OrdinalIgnoreCase);
        var match = _townPattern.Matches(text).LastOrDefault();
        var postcode = FiveDigits().Matches(address).Select(m => m.Value).LastOrDefault(c => _postcodes.ContainsKey(c));
        if (match is null)
        {
            // No town named, but a postcode we know.
            if (postcode is null) return null;
            var pc = _postcodes[postcode];
            return (pc.Place, "", postcode, pc.Point);
        }
        var town = _towns[match.Value];
        // A postcode printed in the address when it belongs to the town; else the town's main post office.
        if (postcode is null || _postcodes[postcode].Town is { } t && !t.Equals(town.Name, StringComparison.OrdinalIgnoreCase))
            postcode = MainPostcode(town.Name);
        return (town.Name, town.Province, postcode ?? "", town.Point);
    }

    /// <summary>The town's general post office code ("Karachi Gpo" 74200 is later than "Karachi City Gpo" 74000: the lowest wins).</summary>
    private string? MainPostcode(string town) =>
        _postcodes.Where(p => string.Equals(p.Value.Town, town, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key).Order(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>zip,city,state,country,latitude,longitude — the API keys these rows by country ("PK:74000").</summary>
    public async Task<int> WriteTableAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = _postcodes.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key},{Quote(p.Value.Place)},PK,PK,{p.Value.Point.Latitude:F5},{p.Value.Point.Longitude:F5}"))
            .Prepend("zip,city,state,country,latitude,longitude").ToList();
        await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(false), ct);
        return lines.Count - 1;
    }

    private static async Task<List<string[]>> ReadZipAsync(ISecClient client, string url, string entry, CancellationToken ct)
    {
        var zip = await client.GetBytesAsync(url, CachePolicy.Immutable, ct) ?? throw new InvalidOperationException($"Couldn't download {url}.");
        using var archive = new ZipArchive(new MemoryStream(zip));
        using var reader = new StreamReader(archive.Entries.First(e => e.Name.Equals(entry, StringComparison.OrdinalIgnoreCase)).Open(), Encoding.UTF8);
        var rows = new List<string[]>();
        while (await reader.ReadLineAsync(ct) is { } line) rows.Add(line.Split('\t'));
        return rows;
    }

    /// <summary>GeoNames admin1 codes for Pakistan.</summary>
    private static string Province(string code) => code switch
    {
        "02" => "Balochistan", "03" => "Khyber Pakhtunkhwa", "04" => "Punjab", "05" => "Sindh",
        "06" => "Azad Kashmir", "07" => "Gilgit-Baltistan", "08" => "Islamabad", _ => ""
    };

    private static string Quote(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    [GeneratedRegex(@"^\d{5}$|\b\d{5}\b")] private static partial Regex FiveDigits();
    [GeneratedRegex(@"\s+(gpo|g\.p\.o|post office|p\.o)\.?$", RegexOptions.IgnoreCase)] private static partial Regex GpoSuffix();
}
