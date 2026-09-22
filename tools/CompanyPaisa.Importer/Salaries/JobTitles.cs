using System.Globalization;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Salaries;

/// <summary>
/// Groups the job titles employers type into their filings: "Sr. Software Engineer", "SENIOR SOFTWARE ENGINEER" and
/// "Senior Software Engineer (JR12345)" are one job. Levels ("II", "Senior", "Staff") stay apart, because they pay differently.
/// </summary>
public static partial class JobTitles
{
    /// <summary>The grouping key: lower case, abbreviations spelled out, requisition codes and punctuation dropped.</summary>
    public static string Key(string title)
    {
        var t = title.ToLowerInvariant().Replace('–', '-').Replace('—', '-');
        t = Parenthesised().Replace(t, " ");                 // "(JR12345)", "(Level 3)"
        t = TrailingCode().Replace(t, "");                   // "- 12345", "#JC-60"
        t = Regex.Replace(t, @"[/,&]", " ");
        t = Regex.Replace(t, @"[^a-z0-9\s\-\.]", " ");
        var words = t.Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries).Select(w => Abbreviations.GetValueOrDefault(w, w));
        return string.Join(' ', words).Trim();
    }

    /// <summary>How a group's title is shown: its most common spelling, in title case when it was typed in capitals.</summary>
    public static string Display(IEnumerable<string> spellings)
    {
        var best = spellings.Select(Clean).GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        if (best.Any(char.IsLower)) return best;
        var cased = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(best.ToLowerInvariant());
        // Roman levels and common initialisms stay in capitals.
        return RomanOrAcronym().Replace(cased, m => m.Value.ToUpperInvariant());
    }

    /// <summary>A title as typed, without the employer's internal codes: "Software Engineer III (20831.45)" → "Software Engineer III".</summary>
    private static string Clean(string title)
    {
        var t = Regex.Replace(title, @"\s*\([^)]*\d[^)]*\)", " ");
        t = TrailingCodeAnyCase().Replace(t.Trim(), "");
        t = Regex.Replace(t, @"\s+", " ").Trim().TrimEnd('-', ',', '#', ' ');
        return t.Length > 0 ? t : title.Trim();
    }

    [GeneratedRegex(@"\s*(?:-|#|–)\s*[a-z]{0,4}[-#]?\d[\w-]*\s*$", RegexOptions.IgnoreCase)] private static partial Regex TrailingCodeAnyCase();

    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.Ordinal)
    {
        ["sr"] = "senior", ["snr"] = "senior", ["jr"] = "junior", ["mgr"] = "manager", ["mngr"] = "manager", ["engr"] = "engineer",
        ["eng"] = "engineer", ["dev"] = "developer", ["assoc"] = "associate", ["asst"] = "assistant", ["dir"] = "director",
        ["admin"] = "administrator", ["tech"] = "technical", ["sw"] = "software", ["swe"] = "software engineer", ["prin"] = "principal",
        ["vp"] = "vice president", ["svp"] = "senior vice president", ["avp"] = "assistant vice president", ["analyt"] = "analytics",
        ["1"] = "i", ["2"] = "ii", ["3"] = "iii", ["4"] = "iv", ["5"] = "v"
    };

    [GeneratedRegex(@"\([^)]*\)")] private static partial Regex Parenthesised();
    [GeneratedRegex(@"\s*(?:-|#|–)\s*[a-z]{0,4}[-#]?\d[\w-]*\s*$")] private static partial Regex TrailingCode();
    [GeneratedRegex(@"\b(?:i{1,3}|iv|v|vi{1,3}|it|qa|ux|ui|ai|ml|hr|sap|aws|erp|crm|etl|bi|vp|svp|avp|ceo|cfo|cto|usa)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanOrAcronym();
}

/// <summary>
/// Decides which public company a filing's employer is. The tax id (EIN) is exact; subsidiaries file under their own EIN
/// and name ("Amazon.com Services LLC", "JPMorgan Chase Bank, N.A."), so a name that starts with the company's name also
/// counts, and <c>data/reference/employer-aliases.csv</c> names the rest ("Google LLC" is Alphabet).
/// </summary>
public sealed partial class EmployerMatcher
{
    private readonly Dictionary<string, string> _byEin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    /// <summary>Company names by their first word, for the "starts with" rule.</summary>
    private readonly Dictionary<string, List<(string Name, string CompanyId)>> _byFirstWord = new(StringComparer.Ordinal);

    public void AddCompany(string companyId, string name, string? ein)
    {
        if (ein is { Length: 9 }) _byEin.TryAdd(ein, companyId);
        var key = NameKey(name);
        if (key.Length < 3) return;
        // Two companies with the same name (a holding company and its bank): neither is matched by name alone.
        if (!_byName.TryAdd(key, companyId) && _byName[key] != companyId) _byName[key] = "";
        var first = key.Split(' ')[0];
        if (!_byFirstWord.TryGetValue(first, out var list)) _byFirstWord[first] = list = [];
        list.Add((key, companyId));
    }

    public void AddAlias(string employerName, string companyId) => _aliases[NameKey(employerName)] = companyId;

    /// <summary>The company id, and how it was matched ("ein", "alias", "name", "subsidiary"), or null.</summary>
    public (string CompanyId, string How)? Match(string employerName, string? fein)
    {
        var ein = fein is null ? "" : new string(fein.Where(char.IsDigit).ToArray());
        if (ein.Length == 9 && _byEin.TryGetValue(ein, out var byEin)) return (byEin, "ein");
        var key = NameKey(employerName);
        if (key.Length == 0) return null;
        if (_aliases.TryGetValue(key, out var alias)) return (alias, "alias");
        if (_byName.TryGetValue(key, out var byName) && byName.Length > 0) return (byName, "name");

        // "amazon com services" starts with "amazon com": the longest company name the employer's name starts with.
        var words = key.Split(' ');
        if (!_byFirstWord.TryGetValue(words[0], out var candidates)) return null;
        foreach (var (name, id) in candidates.OrderByDescending(c => c.Name.Length))
        {
            if (id.Length == 0 || !key.StartsWith(name + " ", StringComparison.Ordinal)) continue;
            var nameWords = name.Split(' ').Length;
            var rest = words.Skip(nameWords).ToList();
            // A one-word company name ("target", "apple") only when the rest says it's a unit of it ("walmart associates").
            if (nameWords == 1 && (name.Length < 5 || !rest.All(SubsidiaryWords.Contains))) continue;
            return (id, "subsidiary");
        }
        return null;
    }

    /// <summary>"The Goldman Sachs Group, Inc." → "goldman sachs"; "Amazon.com Services LLC" → "amazon com services".</summary>
    public static string NameKey(string name)
    {
        var n = name.ToLowerInvariant().Replace("'", "").Replace("’", "");
        n = Regex.Replace(n, @"\s*[/\\]\s*[a-z. ]{0,12}[/\\]?\s*$", "");   // the SEC's state tags: "Wells Fargo & Company/Mn", "US Bancorp \De\"
        n = Regex.Replace(n, @"\b(d/?b/?a|dba)\b.*$", " ");   // "X Inc. dba Y": the legal name
        n = n.Replace(" & ", " and ").Replace("&", "");       // "AT&T" stays one word
        n = Regex.Replace(n, @"\b(?:[a-z]\.){2,}", m => m.Value.Replace(".", "") + " ");   // "U.S.A." → "usa", "N.A." → "na"
        n = Regex.Replace(n, @"[^a-z0-9]+", " ");
        var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] == "the") words.RemoveAt(0);
        while (words.Count > 1 && LegalWords.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> LegalWords =
    [
        "inc", "incorporated", "corp", "corporation", "co", "company", "companies", "llc", "ltd", "limited", "plc", "lp", "llp", "pllc",
        "na", "sa", "ag", "nv", "bv", "gmbh", "holdings", "holding", "group", "the", "pc", "pa", "de", "delaware", "and", "us", "usa"
    ];

    private static readonly HashSet<string> SubsidiaryWords =
    [
        "services", "service", "technologies", "technology", "associates", "stores", "usa", "us", "america", "north", "operations",
        "bank", "digital", "labs", "solutions", "international", "global", "com", "web", "development", "center", "enterprises",
        "software", "health", "healthcare", "financial", "securities", "research", "and", "of", "inc", "llc", "corp", "na", "n", "a",
        "americas", "manufacturing", "logistics", "fulfillment", "retail", "commercial", "capital", "management", "networks", "systems", "markets"
    ];
}
