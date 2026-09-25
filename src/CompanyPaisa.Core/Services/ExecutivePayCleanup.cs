using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// Tidies executive pay rows read from pay tables, the way <see cref="ExecutiveTitles"/> tidies titles: names carrying
/// table debris ("Timothy Pitoniak (", "Mike Nassif (6)", "David E. Benson VP", "Thomas M. Rutledg e"), rows that aren't a
/// person ("Other NEOs Required to Be Discussed"), and one executive counted twice because two filings spell the name
/// differently ("Charles Collier" and "Charlie Collier", both paid $53,304,896 in 2022). Used by the data store when it
/// loads, so data already published is fixed too, and by the importer when it publishes.
/// </summary>
public static partial class ExecutivePayCleanup
{
    /// <summary>What changed, for the importer's report.</summary>
    public sealed record Result(IReadOnlyList<ExecutiveCompensation> Pay, IReadOnlyList<Person> People,
        IReadOnlyDictionary<(string CompanyId, string PersonId), string> Merged, int NamesFixed, int RowsDropped, int DuplicatesRemoved,
        int Misattributed = 0);

    public static Result Apply(IReadOnlyList<ExecutiveCompensation> pay, IReadOnlyList<Person> people)
    {
        var cik = people.GroupBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().SecCik, StringComparer.OrdinalIgnoreCase);

        // 1. Names (and a name that ran on into the title), then rows that aren't a person.
        var namesFixed = 0;
        // A person appears in several years of rows: each distinct name and title is cleaned once.
        var cleanNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var cleanTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<ExecutiveCompensation>(pay.Count);
        var dropped = 0;
        foreach (var row in pay)
        {
            var (name, title) = Rejoin(row.ExecutiveName, row.Title);
            name = cleanNames.TryGetValue(name, out var cn) ? cn : cleanNames[name] = CleanName(name);
            if (!IsPerson(name)) { dropped++; continue; }
            if (name != row.ExecutiveName || title != row.Title) namesFixed++;
            rows.Add(name == row.ExecutiveName && title == row.Title ? row : row with { ExecutiveName = name, Title = title });
        }

        // A row given to the wrong person: two people with every amount identical in one year, where one row's (raw) title
        // holds the other's name — the table's next name ran into this cell and its figures were read with it. AAON's 2026
        // proxy put Gary Fields' 2023 pay under Matthew Tobolski ("Chief Executive Officer Gary D. Fields Special Advisor");
        // a heading read as a name ("Technical University" with the title "Greg E. Jansen Senior Vice President") likewise.
        // Without such evidence both rows stay: co-founders on the same plan are paid the same.
        var misattributed = new HashSet<ExecutiveCompensation>(ReferenceEqualityComparer.Instance);
        foreach (var g in rows.Where(r => r.Total > 0).GroupBy(Package))
        {
            var list = g.DistinctBy(r => r.PersonId, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var x in list)
                if (list.Any(y => y != x && !SamePerson(x.ExecutiveName, y.ExecutiveName) && TitleNames(x.Title, y.ExecutiveName) && !TitleNames(y.Title, x.ExecutiveName)))
                    misattributed.Add(x);
        }
        if (misattributed.Count > 0) rows = rows.Where(r => !misattributed.Contains(r)).ToList();

        // 2. The same executive under two ids at one company: similar names paid exactly the same in the same year.
        var parent = new Dictionary<(string, string), (string, string)>();
        (string, string) Find((string, string) k)
        {
            while (parent.TryGetValue(k, out var p) && p != k) k = p;
            return k;
        }
        foreach (var g in rows.GroupBy(r => (Company: r.CompanyId.ToUpperInvariant(), r.Year)))
        {
            var list = g.DistinctBy(r => r.PersonId, StringComparer.OrdinalIgnoreCase).ToList();
            for (var i = 0; i < list.Count; i++)
                for (var j = i + 1; j < list.Count; j++)
                {
                    var (a, b) = (list[i], list[j]);
                    // The exact same name under two ids is one person whatever the figures (two filings can differ by a rounding);
                    // similar names only when the pay is identical too.
                    var sameName = Letters(a.ExecutiveName) == Letters(b.ExecutiveName);
                    // Identical in every amount, from two different filings, with one surname: one person the filings name
                    // differently ("Kath" / "Kathryn McLay", "Mel Hope" / "Henry Melville Hope"). Brothers or twins on one
                    // plan appear in the same filing.
                    var oneFamily = Package(a) == Package(b) && a.Total > 0 && a.SourceFiling != b.SourceFiling && LastName(a.ExecutiveName) is { Length: >= 3 } la && la == LastName(b.ExecutiveName);
                    if (!sameName && !oneFamily && (a.Total != b.Total || a.Total <= 0 || !SamePerson(a.ExecutiveName, b.ExecutiveName))) continue;
                    // Two different SEC insiders are two people, whatever their names.
                    if (cik.GetValueOrDefault(a.PersonId) is { Length: > 0 } ca && cik.GetValueOrDefault(b.PersonId) is { Length: > 0 } cb && ca != cb) continue;
                    var (ra, rb) = (Find((g.Key.Company, a.PersonId.ToLowerInvariant())), Find((g.Key.Company, b.PersonId.ToLowerInvariant())));
                    if (ra != rb) parent[rb] = ra;
                }
        }
        // The id each merged group keeps: an SEC insider's, else the one with most rows, else the fullest name.
        var rowsById = rows.GroupBy(r => (r.CompanyId.ToUpperInvariant(), r.PersonId.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.ToList());
        var groups = parent.Keys.Concat(parent.Values).Distinct().GroupBy(Find).ToList();
        var merged = new Dictionary<(string CompanyId, string PersonId), string>();
        var nameOf = new Dictionary<(string, string), string>();
        foreach (var grp in groups)
        {
            var members = grp.Where(rowsById.ContainsKey).ToList();
            var keep = members.OrderByDescending(m => cik.GetValueOrDefault(rowsById[m][0].PersonId) is { Length: > 0 })
                .ThenByDescending(m => rowsById[m].Count).ThenByDescending(m => rowsById[m][0].ExecutiveName.Length).First();
            var keepId = rowsById[keep][0].PersonId;
            var fullest = members.SelectMany(m => rowsById[m]).Select(r => r.ExecutiveName).OrderByDescending(n => Letters(n).Length).First();
            foreach (var m in members)
            {
                nameOf[m] = fullest;
                if (m != keep) merged[(rowsById[m][0].CompanyId, rowsById[m][0].PersonId)] = keepId;
            }
        }

        // 3. Rows under the kept id and name; one row per person, company and year (the one with the fuller title).
        var duplicates = 0;
        var result = new List<ExecutiveCompensation>(rows.Count);
        var seen = new Dictionary<(string, string, int), int>();
        foreach (var r in rows)
        {
            var key = (r.CompanyId.ToUpperInvariant(), r.PersonId.ToLowerInvariant());
            var row = merged.TryGetValue((r.CompanyId, r.PersonId), out var to) || nameOf.ContainsKey(key)
                ? r with { PersonId = to ?? r.PersonId, ExecutiveName = nameOf.GetValueOrDefault(key, r.ExecutiveName) }
                : r;
            // The end of a hyphenated name at the start of the title: "Caryn Seidman-Becker" / "Becker Chairman and CEO".
            if (row.ExecutiveName.Contains('-') && row.ExecutiveName.Split('-')[^1] is { Length: > 1 } tail &&
                row.Title.StartsWith(tail + " ", StringComparison.Ordinal))
                row = row with { Title = row.Title[(tail.Length + 1)..] };
            // The surname (and suffix) repeated at the start of the title: "Richard N. Patterson, Jr." / "Patterson, Jr. Chief…".
            if (SurnameAndSuffix().Match(row.ExecutiveName) is { Success: true } sur && row.Title.StartsWith(sur.Value.Trim() + " ", StringComparison.Ordinal))
                row = row with { Title = row.Title[(sur.Value.Trim().Length + 1)..] };
            // Titles are tidied last: until here the raw title is the evidence of a name that ran into the cell.
            row = row with { Title = cleanTitles.TryGetValue(row.Title, out var ct) ? ct : cleanTitles[row.Title] = ExecutiveTitles.Clean(row.Title) };
            var slot = (row.CompanyId.ToUpperInvariant(), row.PersonId.ToLowerInvariant(), row.Year);
            if (seen.TryGetValue(slot, out var at))
            {
                duplicates++;
                if (row.Title.Length > result[at].Title.Length && row.Total == result[at].Total) result[at] = row;
                continue;
            }
            seen[slot] = result.Count;
            result.Add(row);
        }

        // People: names tidied the same way; an id merged away everywhere it appeared is gone.
        var stillUsed = result.Select(r => r.PersonId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergedAway = merged.Keys.Select(k => k.PersonId).Where(id => !stillUsed.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cleanPeople = people.Where(p => !mergedAway.Contains(p.PersonId))
            .Select(p => CleanName(p.Name) is var n && n != p.Name ? p with { Name = n } : p).ToList();
        // A person whose rows all went (not a person after all) keeps no record either.
        var everUsed = pay.Select(r => r.PersonId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        cleanPeople = cleanPeople.Where(p => stillUsed.Contains(p.PersonId) || !everUsed.Contains(p.PersonId)).ToList();
        return new Result(result, cleanPeople, merged, namesFixed, dropped, duplicates, misattributed.Count);
    }

    /// <summary>
    /// "Caryn Seidman-" with the title "Becker Chairman and CEO": the name's last part ran into the title cell.
    /// </summary>
    public static (string Name, string Title) Rejoin(string name, string title)
    {
        var n = name.TrimEnd();
        if (n.EndsWith('-') && Regex.Match(title.TrimStart(), @"^(\p{Lu}[\p{Ll}'’]+)\s+(.+)$") is { Success: true } m)
            return (n + m.Groups[1].Value, m.Groups[2].Value);
        // "Wes Powell -President" with the title "and Chief Executive Officer": the title's start is in the name.
        if (DashTitle().Match(n) is { Success: true } dash && Regex.IsMatch(title.TrimStart(), @"^(and|&|of|,)\b|^,"))
            return (n[..dash.Index], dash.Value.Trim().TrimStart('-').Trim() + " " + title.TrimStart());
        return (name, title);
    }

    /// <summary>A name without footnote markers, stray brackets and punctuation, role words or a split last word.</summary>
    public static string CleanName(string name)
    {
        var n = Spaces().Replace(name ?? "", " ").Trim();
        n = Superscripts().Replace(n, "");
        n = Footnote().Replace(n, "");            // "Amy M. Rocklin (1)", "David C.Dauch1"
        n = OpenBracket().Replace(n, "");         // "Timothy Pitoniak (", "Brett Adcock (Former"
        n = LeadingCompany().Replace(n, "");      // "First Financial Corporation Rodger A. McHargue"
        n = TrailingRole().Replace(n, "");        // "David E. Benson VP", "J. R. LUCIANO Board", "Jonathan Fitzpatrick Non"
        n = SplitLastWord().Replace(n, "$1$2");   // "Thomas M. Rutledg e"
        n = DashTitle().Replace(n, "");          // "Wes Powell -President", "John C. Watts -Vice"
        n = RoleTail(n);                          // "Jeffrey Hoover CLO and", "Michael McElhaugh Current"
        n = n.Trim().TrimEnd(':', ',', ';', '-', '–', '—', '*', '†', '‡', '/').TrimStart(':', ',', ';', '-', '*', '(', ')').Trim();
        return n.Length == 0 ? (name ?? "").Trim() : n;
    }

    /// <summary>
    /// A name followed by the start of its title ("Philip B. Flynn Special Advisor and", "Mandy Yang VP and", "Ravi Kumar
    /// Current"): cut at the first role word after the name's first two words. A label with no name before it ("Current
    /// NEOs", "Counsel and") is left for <see cref="IsPerson"/> to reject.
    /// </summary>
    private static string RoleTail(string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 2; i < tokens.Length; i++)
            if (RoleWord().IsMatch(tokens[i].TrimEnd(',', ':', ';').Split('-')[0]))   // "SEVP-Chief", "Co-Chief" too
                return string.Join(' ', tokens[..i]);
        return name;
    }

    /// <summary>False for a table's row labels read as a name ("Other NEOs Required to Be Discussed", "Counsel and").</summary>
    public static bool IsPerson(string name)
    {
        var n = name.Trim();
        if (Letters(n).Length < 3) return false;
        if (NotAPerson().IsMatch(n)) return false;
        // "Strategy and Transformation", "Marketing &": headings, not names.
        if (Regex.IsMatch(n, @"\b(and|of|the|for)$| (and|&) |&$", RegexOptions.IgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// True when two names are the same person written two ways: the same letters ("Rutledg e"), one cut short
    /// ("Caryn Seidman" / "Caryn Seidman-Becker"), a nickname ("Doug" / "C. Douglas" McMillon), a middle name used as the
    /// first ("Rupert Murdoch" / "K. Rupert Murdoch"), or a one-letter typo in a long first or last name.
    /// </summary>
    public static bool SamePerson(string a, string b)
    {
        var ka = Letters(a);
        var kb = Letters(b);
        if (ka.Length == 0 || kb.Length == 0) return false;
        if (ka == kb) return true;
        if (Math.Min(ka.Length, kb.Length) >= 6 && (ka.StartsWith(kb, StringComparison.Ordinal) || kb.StartsWith(ka, StringComparison.Ordinal))) return true;
        var wa = Words(a);
        var wb = Words(b);
        if (wa.Count < 2 || wb.Count < 2) return false;
        var (fa, la, fb, lb) = (wa[0], wa[^1], wb[0], wb[^1]);
        if (la == lb && la.Length >= 3)
        {
            if (fa == fb || Nickname(fa, fb)) return true;
            // "J. Duato" / "Joaquin Duato".
            if ((fa.Length == 1 && fb.StartsWith(fa, StringComparison.Ordinal)) || (fb.Length == 1 && fa.StartsWith(fb, StringComparison.Ordinal))) return true;
            if (fa.Length >= 4 && fb.Length >= 4 && Distance(fa, fb) <= 1) return true;
            // One uses a middle name the other leads with: "C. Douglas McMillon" / "Doug McMillon" (via the nickname),
            // "K. Rupert Murdoch" / "Rupert Murdoch".
            if (wa.Skip(1).Take(wa.Count - 2).Any(w => w.Length > 1 && (w == fb || Nickname(w, fb))) || wb.Skip(1).Take(wb.Count - 2).Any(w => w.Length > 1 && (w == fa || Nickname(w, fa)))) return true;
        }
        return fa == fb && fa.Length >= 3 && Math.Min(la.Length, lb.Length) >= 5 && Distance(la, lb) <= 1;
    }

    private static (string, int, decimal, decimal, decimal, decimal, decimal) Package(ExecutiveCompensation r) =>
        (r.CompanyId.ToUpperInvariant(), r.Year, r.Salary, r.Bonus, r.StockAwards, r.Other, r.Total);

    private static string LastName(string name) => Words(name) is { Count: > 0 } w ? w[^1] : "";

    /// <summary>True when a title contains someone's name: their surname and their first name or its initial.</summary>
    private static bool TitleNames(string title, string name)
    {
        var w = Words(name);
        if (w.Count < 2 || w[^1].Length < 3) return false;
        var t = Words(title);
        var at = t.IndexOf(w[^1]);
        return at > 0 && t.Take(at).Any(x => x == w[0] || (x.Length == 1 && x[0] == w[0][0]));
    }

    // Letters and digits: footnote numbers are gone by now, so "Sample Executive 001" and "…002" stay two people.
    // Both are asked for the same names many times over (every pair in a company-year), so each name is worked out once.
    private static string Letters(string name) =>
        LettersCache.GetOrAdd(name, n => NotLetters().Replace(n.ToLowerInvariant(), ""));

    private static List<string> Words(string name) =>
        WordsCache.GetOrAdd(name, n => WordPattern().Matches(Honorifics().Replace(n.ToLowerInvariant(), " ")).Select(m => m.Value).ToList());

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LettersCache = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<string>> WordsCache = new(StringComparer.Ordinal);

    [GeneratedRegex(@"[^\p{L}\p{Nd}]")] private static partial Regex NotLetters();

    private static readonly string[][] Nicknames =
    [
        ["william", "bill", "will", "billy"], ["robert", "bob", "rob", "bobby"], ["richard", "rick", "dick", "rich"], ["james", "jim", "jimmy", "jamie"],
        ["michael", "mike"], ["charles", "charlie", "chuck"], ["douglas", "doug"], ["thomas", "tom"], ["joseph", "joe"], ["edward", "ed", "ted", "eddie"],
        ["anthony", "tony"], ["christopher", "chris"], ["daniel", "dan", "danny"], ["david", "dave"], ["jonathan", "jon"], ["john", "jack"],
        ["matthew", "matt", "mathew"], ["nicholas", "nick"], ["patrick", "pat"], ["peter", "pete"], ["stephen", "steve", "steven"], ["timothy", "tim"],
        ["gregory", "greg"], ["jeffrey", "jeff", "geoffrey"], ["kenneth", "ken"], ["ronald", "ron"], ["donald", "don"], ["benjamin", "ben"],
        ["benito", "ben"], ["samuel", "sam"], ["andrew", "andy", "drew"], ["alexander", "alex"], ["elizabeth", "liz", "beth", "betsy"],
        ["katherine", "kate", "kathy", "catherine", "cathy", "katie"], ["margaret", "maggie", "peggy"], ["jennifer", "jen", "jenny"], ["susan", "sue"],
        ["deborah", "debbie", "debra"], ["frederick", "fred"], ["lawrence", "larry"], ["raymond", "ray"], ["gerald", "jerry"], ["philip", "phil"],
        ["zachary", "zach"], ["vasily", "basil"], ["leonard", "len"], ["theodore", "ted", "theo"], ["henry", "hank"], ["francis", "frank"],
        ["kimberly", "kim"], ["rebecca", "becky"], ["victoria", "vicky"], ["pamela", "pam"], ["patricia", "trish"], ["joshua", "josh"],
        ["nathaniel", "nate"], ["randall", "randy"], ["russell", "russ"], ["walter", "walt"], ["eugene", "gene"], ["harold", "hal"], ["martin", "marty"]
    ];

    private static bool Nickname(string a, string b) => a != b && Nicknames.Any(g => g.Contains(a) && g.Contains(b));

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    [GeneratedRegex(@"[\s  -​  　﻿]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[¹²³⁰-₟]+")] private static partial Regex Superscripts();
    [GeneratedRegex(@"\s*\(\s*[\d*†‡§,\s]+\s*\)|(?<=\p{L})\d+\b|\s+\d{1,2}$")] private static partial Regex Footnote();
    [GeneratedRegex(@"\s*\([^)]*$")] private static partial Regex OpenBracket();
    [GeneratedRegex(@"\s*:?\s+(VP|SVP|EVP|CEO|CFO|COO|CTO|Board|Board\s+Member|Non|Director|Chair|Chairman)\s*:?$")] private static partial Regex TrailingRole();
    [GeneratedRegex(@"(\p{Ll}{2,}) (\p{Ll})$")] private static partial Regex SplitLastWord();
    [GeneratedRegex(@"^(current|former|global|special|senior|advisor|adviser|strategic|general|chairperson|chairman|chairwoman|chair|vc|vp|svp|evp|clo|cio|cfo|ceo|coo|cto|cao|cco|cmo|chro|pfo|peo|sevp|&|president|director|manager|officer|executive|board|member|non|retired|interim|prior|co|and|of|to|the)$", RegexOptions.IgnoreCase)]
    private static partial Regex RoleWord();
    [GeneratedRegex(@"\b(other|neos?|named\s+executive|required|total|current|former|all\s+executive|executive\s+officers|directors|officers|board\s+member|compensation|salary|bonus|fiscal|awards?|airlines?|inc|corp|corporation|llc|company|bancorp)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAPerson();
    /// <summary>" -President", " -Chief Executive": the title after a dash with a space before it (not "Najm-ul- Hassan").</summary>
    [GeneratedRegex(@"\s+-\s*\p{Lu}.*$")] private static partial Regex DashTitle();
    /// <summary>A name's last word with any suffix: "Patterson, Jr.", "Hickey III", "Fields".</summary>
    [GeneratedRegex(@"\s\p{Lu}[\p{L}'’-]+(,?\s+(Jr\.?|Sr\.?|II|III|IV))?$")] private static partial Regex SurnameAndSuffix();
    /// <summary>A company's name in front of the person's: "First Financial Corporation Rodger A. McHargue".</summary>
    [GeneratedRegex(@"^.*\b(Corporation|Company|Bancorp|Inc)\.?\s+(?=\p{Lu}[\p{L}.'’-]*\s+\p{Lu})")] private static partial Regex LeadingCompany();
    [GeneratedRegex(@"\b(mr|mrs|ms|dr|jr|sr|ii|iii|iv|phd|md|esq|cpa)\b\.?")] private static partial Regex Honorifics();
    [GeneratedRegex(@"\p{L}+(['’-]\p{L}+)*")] private static partial Regex WordPattern();
}
