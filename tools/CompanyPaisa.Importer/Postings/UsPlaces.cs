using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Postings;

/// <summary>
/// The US places a job ad names, however it writes them: "Santa Clara, CA", "US, CA, Santa Clara" (Workday),
/// "New York, New York, United States", "Austin, TX; Remote - US". Places are put on the map from the ZIP code table's
/// town names.
/// </summary>
public sealed partial class UsPlaces
{
    private readonly Dictionary<(string City, string State), GeoPoint> _towns = new();

    /// <summary>Town centres from data/reference/us-zip-centroids.csv (zip,city,state,latitude,longitude).</summary>
    public static UsPlaces Load(string zipTablePath)
    {
        var places = new UsPlaces();
        if (!File.Exists(zipTablePath)) return places;
        var sums = new Dictionary<(string, string), (double Lat, double Lng, int N)>();
        foreach (var line in File.ReadLines(zipTablePath).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length < 5 || !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            var key = (Town(f[1]), f[2].Trim().ToUpperInvariant());
            var s = sums.GetValueOrDefault(key);
            sums[key] = (s.Lat + lat, s.Lng + lng, s.N + 1);
        }
        foreach (var (key, s) in sums) places._towns[key] = new GeoPoint(Math.Round(s.Lat / s.N, 4), Math.Round(s.Lng / s.N, 4));
        return places;
    }

    public GeoPoint? Locate(string city, string state) => _towns.TryGetValue((Town(city), state), out var p) ? p : null;

    /// <summary>The (city, state code) pairs in a location text; empty when it names no US city.</summary>
    public static List<(string City, string State)> Parse(string? location)
    {
        var found = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(location)) return found;
        foreach (var raw in Separators().Split(location))
        {
            var part = Regex.Replace(raw, @"\((?:hq|headquarters|onsite|on-site|hybrid|remote|in office)[^)]*\)", "", RegexOptions.IgnoreCase).Trim(' ', ',', '-');
            if (part.Length == 0) continue;
            // Workday: "US, CA, Santa Clara" or "USA-CA-Santa Clara"
            var wd = WorkdayStyle().Match(part);
            if (wd.Success && StateCode(wd.Groups["st"].Value) is { } wdState)
            {
                if (!IsRemote(wd.Groups["city"].Value)) found.Add((Tidy(wd.Groups["city"].Value), wdState));   // "US, OR, Remote": no city
                continue;
            }
            var bits = part.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
            while (bits.Count > 0 && IsCountry(bits[^1])) bits.RemoveAt(bits.Count - 1);
            if (bits.Count >= 2 && StateCode(bits[^1]) is { } state && !IsRemote(bits[^2])) found.Add((Tidy(bits[^2]), state));
        }
        return found.Distinct().ToList();
    }

    /// <summary>A job in the US: a US city, or remote / anywhere in the United States.</summary>
    public static bool IsUs(string? location) =>
        location is not null && (Parse(location).Count > 0 || UsWords().IsMatch(location));

    private static bool IsCountry(string s) => Regex.IsMatch(s, @"^(?:us|usa|u\.s\.a?\.?|united states(?: of america)?)$", RegexOptions.IgnoreCase);
    private static bool IsRemote(string s) => s.Contains("remote", StringComparison.OrdinalIgnoreCase);

    private static string Tidy(string city)
    {
        var c = Regex.Replace(city, @"\s+", " ").Trim();
        c = Regex.Replace(c, @"^(?:remote|hybrid)\s*[-–:]\s*", "", RegexOptions.IgnoreCase);
        return c.Equals("New York City", StringComparison.OrdinalIgnoreCase) || c.Equals("NYC", StringComparison.OrdinalIgnoreCase) ? "New York" : Geo.Text.TitleCase(c);
    }

    private static string Town(string city) => Regex.Replace(city.ToLowerInvariant().Replace("saint ", "st ").Replace("st. ", "st "), @"[^a-z ]", "").Trim();

    public static string? StateCode(string s)
    {
        var t = s.Trim().TrimEnd('.');
        if (t.Length == 2 && Names.ContainsKey(t.ToUpperInvariant())) return t.ToUpperInvariant();
        return Codes.GetValueOrDefault(t.ToLowerInvariant());
    }

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["AL"] = "alabama", ["AK"] = "alaska", ["AZ"] = "arizona", ["AR"] = "arkansas", ["CA"] = "california", ["CO"] = "colorado",
        ["CT"] = "connecticut", ["DE"] = "delaware", ["DC"] = "district of columbia", ["FL"] = "florida", ["GA"] = "georgia", ["HI"] = "hawaii",
        ["ID"] = "idaho", ["IL"] = "illinois", ["IN"] = "indiana", ["IA"] = "iowa", ["KS"] = "kansas", ["KY"] = "kentucky", ["LA"] = "louisiana",
        ["ME"] = "maine", ["MD"] = "maryland", ["MA"] = "massachusetts", ["MI"] = "michigan", ["MN"] = "minnesota", ["MS"] = "mississippi",
        ["MO"] = "missouri", ["MT"] = "montana", ["NE"] = "nebraska", ["NV"] = "nevada", ["NH"] = "new hampshire", ["NJ"] = "new jersey",
        ["NM"] = "new mexico", ["NY"] = "new york", ["NC"] = "north carolina", ["ND"] = "north dakota", ["OH"] = "ohio", ["OK"] = "oklahoma",
        ["OR"] = "oregon", ["PA"] = "pennsylvania", ["RI"] = "rhode island", ["SC"] = "south carolina", ["SD"] = "south dakota",
        ["TN"] = "tennessee", ["TX"] = "texas", ["UT"] = "utah", ["VT"] = "vermont", ["VA"] = "virginia", ["WA"] = "washington",
        ["WV"] = "west virginia", ["WI"] = "wisconsin", ["WY"] = "wyoming", ["PR"] = "puerto rico"
    };

    private static readonly Dictionary<string, string> Codes = Names.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    [GeneratedRegex(@"\s*(?:;|\||/| or |\n)\s*")] private static partial Regex Separators();
    [GeneratedRegex(@"^(?:US|USA)\s*[,\-]\s*(?<st>[A-Za-z]{2})\s*[,\-]\s*(?<city>[^,]+)$")] private static partial Regex WorkdayStyle();
    [GeneratedRegex(@"\b(?:united states|usa|u\.s\.|remote\s*[-–(,]?\s*us\b|us\s*[-–(,]?\s*remote|anywhere in the us|nationwide)", RegexOptions.IgnoreCase)]
    private static partial Regex UsWords();
}
