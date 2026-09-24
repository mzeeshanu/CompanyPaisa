using System.Globalization;
using System.Text;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// A whole country ("US", "UK", "PK") or one US state / Canadian province ("US-TX", "CA-ON"): a search for "every company
/// here" rather than within a radius.
/// </summary>
public sealed record Region(string Code, string Name, RegionKind Kind, string Country)
{
    /// <summary>The state or province code inside the country ("TX"); null for a country.</summary>
    public string? State => Kind == RegionKind.State ? Code[(Code.IndexOf('-') + 1)..] : null;

    /// <summary>How the website's address names it: "texas", "united-kingdom", "new-york-state".</summary>
    public string Slug => (Code is "US-NY" or "US-WA" ? $"{Name} state" : Name).ToLowerInvariant().Replace(' ', '-');

    /// <summary>The name as it reads in a sentence: "in the United States", "in Texas".</summary>
    public string InSentence => Code is "US" or "UK" or "NL" ? $"the {Name}" : Name;

    public bool Contains(CompanyLocation l) =>
        Regions.CountryOf(l) == Country && (State is null || string.Equals(l.State, State, StringComparison.OrdinalIgnoreCase));

    public RegionDto ToDto() => new(Code, Name, Kind, Country, Slug, InSentence);
}

/// <summary>
/// The countries, US states and Canadian provinces the data covers, and the names people type for them ("Texas", "TX",
/// "UK", "Britain", "Holland"). Locations store a state or province code in the Americas and the country code elsewhere
/// (British, European and Pakistani companies; SEC filers based in Australia).
/// </summary>
public static class Regions
{
    private static readonly (string Code, string Name)[] Countries =
    [
        ("US", "United States"), ("CA", "Canada"), ("UK", "United Kingdom"), ("FR", "France"), ("NL", "Netherlands"),
        ("IT", "Italy"), ("ES", "Spain"), ("AU", "Australia"), ("NZ", "New Zealand"), ("PK", "Pakistan"),
    ];

    private static readonly Dictionary<string, string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California",
        ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia", ["FL"] = "Florida",
        ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa",
        ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
        ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota", ["MS"] = "Mississippi", ["MO"] = "Missouri",
        ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire", ["NJ"] = "New Jersey",
        ["NM"] = "New Mexico", ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio",
        ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["PR"] = "Puerto Rico", ["RI"] = "Rhode Island",
        ["SC"] = "South Carolina", ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
        ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin",
        ["WY"] = "Wyoming",
    };

    private static readonly Dictionary<string, string> CanadianProvinces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AB"] = "Alberta", ["BC"] = "British Columbia", ["MB"] = "Manitoba", ["NB"] = "New Brunswick",
        ["NL"] = "Newfoundland and Labrador", ["NS"] = "Nova Scotia", ["NT"] = "Northwest Territories", ["NU"] = "Nunavut",
        ["ON"] = "Ontario", ["PE"] = "Prince Edward Island", ["QC"] = "Quebec", ["SK"] = "Saskatchewan", ["YT"] = "Yukon",
    };

    /// <summary>Other names for a region, by its code.</summary>
    private static readonly (string Alias, string Code)[] Aliases =
    [
        ("usa", "US"), ("us", "US"), ("u s", "US"), ("u s a", "US"), ("america", "US"), ("united states of america", "US"),
        ("uk", "UK"), ("u k", "UK"), ("gb", "UK"), ("great britain", "UK"), ("britain", "UK"), ("england", "UK"),
        ("holland", "NL"), ("the netherlands", "NL"), ("aus", "AU"), ("nz", "NZ"),
        ("new york state", "US-NY"), ("state of new york", "US-NY"), ("washington state", "US-WA"), ("state of washington", "US-WA"),
        ("newfoundland", "CA-NL"), ("pei", "CA-PE"), ("quebec province", "CA-QC"),
    ];

    /// <summary>
    /// Names that are first of all a city: "New York" and "Washington" go to the city search (type "New York state" or "NY"
    /// for the state).
    /// </summary>
    private static readonly HashSet<string> CityFirst = ["new york", "washington"];

    private static readonly Dictionary<string, Region> ByCode = Build();
    private static readonly Dictionary<string, Region> ByName = BuildNames();

    public static IReadOnlyCollection<Region> All => ByCode.Values;

    /// <summary>"US-TX", "UK", "ca-on".</summary>
    public static Region? FromCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : ByCode.GetValueOrDefault(code.Trim().ToUpperInvariant());

    /// <summary>
    /// What a visitor typed ("Texas", "tx", "united-kingdom", "Québec"), or null when it isn't a region. A country after the
    /// name picks that country's state or province: "Washington, USA" is the state (plain "Washington" is the city), "Ontario, Canada".
    /// </summary>
    public static Region? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = Normalise(text);
        if (!CityFirst.Contains(key) && (ByName.GetValueOrDefault(key) ?? FromCode(text.Trim())) is { } region) return region;
        return SplitCountry(text) is var (rest, country) ? StateIn(country, rest) : null;
    }

    /// <summary>
    /// A place name followed by its country — "Washington, USA", "Dallas US", "Ontario, Canada" — split into the name and the
    /// country; null when the text doesn't end with a country (or is only a country).
    /// </summary>
    public static (string Place, Region Country)? SplitCountry(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = Normalise(text);
        foreach (var (name, country) in CountryNames)
        {
            if (key.Length <= name.Length + 1 || !key.EndsWith(" " + name, StringComparison.Ordinal)) continue;
            // Keep the place as typed ("Toronto", not "toronto"): drop words from the end until they spell the country.
            var comma = text.LastIndexOf(',');
            var rest = comma > 0 ? text[..comma] : null;
            if (rest is null)
            {
                var words = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var n = 1; n < words.Length && rest is null; n++)
                    if (Normalise(string.Join(' ', words[^n..])) == name) rest = string.Join(' ', words[..^n]);
            }
            return string.IsNullOrWhiteSpace(rest) ? null : (rest.Trim(), country);
        }
        return null;
    }

    /// <summary>The state or province named like the city a name usually means: "Washington" → Washington state, "New York" → New York state.</summary>
    public static Region? StateAlsoNamed(string? text) =>
        text is not null && CityFirst.Contains(Normalise(text)) ? ByName.Values.FirstOrDefault(r => r.Kind == RegionKind.State && Normalise(r.Name) == Normalise(text)) : null;

    /// <summary>A state or province of <paramref name="country"/> by name or code ("Washington", "WA", "Ontario", "ON").</summary>
    private static Region? StateIn(Region country, string name)
    {
        var key = Normalise(name);
        return ByCode.Values.FirstOrDefault(r => r.Kind == RegionKind.State && r.Country == country.Code
                                                 && (Normalise(r.Name) == key || Normalise(r.State!) == key))
               ?? (ByName.GetValueOrDefault(key) is { Kind: RegionKind.State } alias && alias.Country == country.Code ? alias : null);
    }

    /// <summary>Every name and code a country goes by, longest first so "united states of america" wins over "america".</summary>
    private static readonly (string Name, Region Country)[] CountryNames = BuildCountryNames();

    /// <summary>
    /// A location's country. In the Americas the state column holds a US state or Canadian province code; elsewhere it holds
    /// the country ("UK", "FR", "PK", "AU"). Longitude settles the one clash: "NL" is Newfoundland at -53°, the Netherlands at 5°.
    /// </summary>
    public static string CountryOf(CompanyLocation l)
    {
        if (l.Point.Longitude < -30)
        {
            if (UsStates.ContainsKey(l.State)) return "US";
            if (CanadianProvinces.ContainsKey(l.State)) return "CA";
        }
        return l.State.ToUpperInvariant();
    }

    private static Dictionary<string, Region> Build()
    {
        var all = new Dictionary<string, Region>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in Countries) all[code] = new Region(code, name, RegionKind.Country, code);
        foreach (var (code, name) in UsStates) all[$"US-{code}"] = new Region($"US-{code}", name, RegionKind.State, "US");
        foreach (var (code, name) in CanadianProvinces) all[$"CA-{code}"] = new Region($"CA-{code}", name, RegionKind.State, "CA");
        return all;
    }

    private static Dictionary<string, Region> BuildNames()
    {
        var names = new Dictionary<string, Region>();
        // Two-letter codes, later ones winning a clash: Canadian provinces, then countries (NL = the Netherlands, not
        // Newfoundland), then US states (CA = California; Canada is typed in full).
        foreach (var r in ByCode.Values.Where(r => r.Kind == RegionKind.State && r.Country == "CA")) names[Normalise(r.State!)] = r;
        foreach (var r in ByCode.Values.Where(r => r.Kind == RegionKind.Country)) names[Normalise(r.Code)] = r;
        foreach (var r in ByCode.Values.Where(r => r.Kind == RegionKind.State && r.Country == "US")) names[Normalise(r.State!)] = r;
        foreach (var r in ByCode.Values) names[Normalise(r.Name)] = r;
        foreach (var (alias, code) in Aliases) names[Normalise(alias)] = ByCode[code];
        return names;
    }

    private static (string, Region)[] BuildCountryNames() =>
        ByName.Where(p => p.Value.Kind == RegionKind.Country).Select(p => (p.Key, p.Value)).OrderByDescending(p => p.Key.Length).ToArray();

    /// <summary>Lower case, no accents or punctuation, single spaces: "Québec" → "quebec", "united-kingdom" → "united kingdom".</summary>
    private static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
