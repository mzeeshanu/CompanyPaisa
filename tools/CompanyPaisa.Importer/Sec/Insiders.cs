using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Sec;

/// <summary>A person who filed insider-ownership reports for a company. <see cref="Name"/> is in SEC order: "Last First Middle".</summary>
public sealed record SecInsider(long Cik, string Name, DateOnly? LastFiling, string Role)
{
    public bool IsOfficer => Role.Contains("officer", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads the "Ownership Reports from" table of EDGAR's issuer page (/cgi-bin/own-disp?action=getissuer).</summary>
internal static partial class InsiderParser
{
    [GeneratedRegex("""<tr>\s*<td[^>]*>\s*<a href="/cgi-bin/own-disp\?action=getowner&amp;CIK=(\d+)">([^<]+)</a>\s*</td>\s*<td>.*?</td>\s*<td>([\d-]*)</td>\s*<td>([^<]*)</td>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex OwnerRow();

    public static IReadOnlyList<SecInsider> Parse(string html)
    {
        var list = new List<SecInsider>();
        var seen = new HashSet<long>();
        foreach (Match m in OwnerRow().Matches(html))
        {
            if (!long.TryParse(m.Groups[1].Value, out var cik) || !seen.Add(cik)) continue;
            DateOnly? last = DateOnly.TryParseExact(m.Groups[3].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new SecInsider(cik, WebUtility.HtmlDecode(m.Groups[2].Value).Trim(), last, WebUtility.HtmlDecode(m.Groups[4].Value).Trim()));
        }
        return list;
    }
}

/// <summary>
/// Matches a name printed in a proxy statement ("Steven R. Fife") to the company's insider list ("Fife Steven R"),
/// so the same person gets the same id at every company — and two different "John Smith"s never merge.
/// </summary>
internal static partial class InsiderMatcher
{
    [GeneratedRegex(@"\b(jr|sr|ii|iii|iv|md|m\.d|phd|ph\.d|cpa|esq)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\(.*?\)|[“”""].*?[“”""]")]
    private static partial Regex Nickname();

    private static readonly Dictionary<string, string[]> Nicknames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bob"] = ["robert"], ["rob"] = ["robert"], ["bill"] = ["william"], ["will"] = ["william"], ["jim"] = ["james"],
        ["jamie"] = ["james"], ["mike"] = ["michael"], ["tom"] = ["thomas"], ["tim"] = ["timothy"], ["dave"] = ["david"],
        ["dan"] = ["daniel"], ["danny"] = ["daniel"], ["joe"] = ["joseph"], ["chris"] = ["christopher", "christine", "christina"],
        ["matt"] = ["matthew"], ["steve"] = ["steven", "stephen"], ["rick"] = ["richard"], ["dick"] = ["richard"],
        ["rich"] = ["richard"], ["tony"] = ["anthony"], ["andy"] = ["andrew"], ["drew"] = ["andrew"], ["ed"] = ["edward"],
        ["ted"] = ["edward", "theodore"], ["greg"] = ["gregory"], ["jeff"] = ["jeffrey"], ["ken"] = ["kenneth"],
        ["larry"] = ["lawrence"], ["pat"] = ["patrick", "patricia"], ["pete"] = ["peter"], ["sam"] = ["samuel"],
        ["sandy"] = ["sandra"], ["kathy"] = ["katherine", "kathleen"], ["kate"] = ["katherine"], ["liz"] = ["elizabeth"],
        ["beth"] = ["elizabeth"], ["jen"] = ["jennifer"], ["jenny"] = ["jennifer"], ["sue"] = ["susan"], ["doug"] = ["douglas"],
        ["don"] = ["donald"], ["ron"] = ["ronald"], ["charlie"] = ["charles"], ["chuck"] = ["charles"], ["fred"] = ["frederick"],
        ["jack"] = ["john"], ["jon"] = ["jonathan"], ["nick"] = ["nicholas"], ["ben"] = ["benjamin"], ["alex"] = ["alexander", "alexandra"],
        ["phil"] = ["phillip", "philip"], ["ray"] = ["raymond"], ["gene"] = ["eugene"], ["jerry"] = ["gerald", "jerome"],
        ["terry"] = ["terrence", "terence"], ["vince"] = ["vincent"], ["walt"] = ["walter"], ["zach"] = ["zachary"],
    };

    /// <summary>Lower-case name words without suffixes, nicknames in quotes/brackets, initials or punctuation.</summary>
    internal static List<string> Words(string name)
    {
        var n = Nickname().Replace(name.Replace('’', '\''), " ");
        n = Suffix().Replace(n, " ").ToLowerInvariant().Replace("-", "").Replace("'", "");
        return Regex.Replace(n, @"[^a-z\s]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 1).ToList();
    }

    public static SecInsider? Match(string proxyName, IReadOnlyList<SecInsider> insiders)
    {
        var words = Words(proxyName);
        if (words.Count < 2 || insiders.Count == 0) return null;
        var (first, last) = (words[0], words[^1]);
        var firsts = Nicknames.TryGetValue(first, out var formal) ? [first, .. formal] : new[] { first };

        // SEC order is "Last First Middle": the surname is usually the first word, but compound surnames spread out.
        var sameSurname = insiders.Where(i => Words(i.Name) is { Count: > 0 } w && (w[0] == last || w.Contains(last))).ToList();
        var exact = sameSurname.Where(i => Words(i.Name).Skip(1).Any(w => firsts.Contains(w))).ToList();
        var pick = Pick(exact);
        if (pick is not null || exact.Count > 1) return pick;

        // "Bob Smith" vs "Smith Robert": accept a first-initial match only when that surname is unique at the company.
        var initial = sameSurname.Where(i => Words(i.Name).Skip(1).Any(w => w[0] == first[0])).ToList();
        return initial.Count == 1 ? initial[0] : null;
    }

    private static SecInsider? Pick(List<SecInsider> candidates) => candidates.Count switch
    {
        0 => null,
        1 => candidates[0],
        _ => candidates.Count(c => c.IsOfficer) == 1 ? candidates.First(c => c.IsOfficer) : null
    };
}
