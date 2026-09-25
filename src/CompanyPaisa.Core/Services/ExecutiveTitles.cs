using System.Text.RegularExpressions;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// Tidies executive titles read from proxy statements' pay tables. The table's footnotes often run into the title cell
/// ("Chief Financial Officer Represents the aggregate grant date fair value of option awards…"), and sometimes the next
/// person's name, age and title do ("Former President and CEO Mark Hernandez , 57 President and CEO"). Used by the
/// importer when it reads a filing and by the data store when it loads, so titles already published are fixed too.
/// </summary>
public static partial class ExecutiveTitles
{
    /// <summary>What a title that was only footnote text becomes.</summary>
    public const string Fallback = "Named Executive Officer";

    /// <summary>Longer than any real title in the data; anything past it is cut at a word and marked "…".</summary>
    public const int MaxLength = 100;

    public static string Clean(string? title)
    {
        var t = Spaces().Replace(title ?? "", " ").Trim();
        // Punctuation or the "age" column left in front: ". Chief Operating Officer", "-Executive Vice President", "age 59".
        t = LeadingDebris().Replace(t, "");
        // The person's own name (or a year) in front of the title: "James L. Dolan Executive Chairman…" → "Executive Chairman…".
        t = LeadingName().Replace(t, "");
        if (FootnoteAtStart().IsMatch(t)) return Fallback;
        var nextPerson = NameWithInitial();
        foreach (var cut in new[] { FootnoteStart(), NameAndAge(), nextPerson, Amounts() })
        {
            if (cut.Match(t) is not { Success: true, Index: > 0 } m) continue;
            // "Officer Kevin M. Speirits Former Interim CFO": one stray word from the row above, then the name, then the title.
            var before = t[..m.Index].Trim();
            t = cut == nextPerson && !before.Contains(' ') && t[(m.Index + m.Length)..].Trim() is { Length: > 3 } after ? after : before;
        }
        t = SectionLabel().Replace(t.TrimEnd(' ', ',', ';', ':', '-', '–', '—', '('), "").TrimEnd(' ', ',', ';', ':', '-', '–', '—', '(');
        // A closing bracket whose opening one went with the name: "Former Co-CEO)".
        if (t.EndsWith(')') && !t.Contains('(')) t = t.TrimEnd(')').TrimEnd();
        // "former Chief Innovation Officer": titles start with a capital.
        if (t.Length > 0 && char.IsLower(t[0])) t = char.ToUpperInvariant(t[0]) + t[1..];
        if (t.Length > MaxLength)
        {
            var cut = t.LastIndexOf(' ', MaxLength);
            t = t[..(cut > MaxLength / 2 ? cut : MaxLength)].TrimEnd(' ', ',', ';', ':', '-', '–') + "…";
        }
        return t.Length == 0 ? Fallback : t;
    }

    /// <summary>Words that begin a footnote and never appear in a job title.</summary>
    private const string Footnote =
        @"(?:Represents?\b|Aggregate\s+grant|Amounts?\s+(?:in|shown|reported|reflect|represent|include|for|paid|disclosed|listed)\b" +
        @"|Amounts?\b(?=[^,]{0,40}\b(?:column|reflect|represent|include)\b)" +
        @"|The\s+(?:amounts?|values?|figures|dollar|grant|aggregate|compensation|salary|awards?|bonus|amount)\b" +
        @"|These\s+(?:values|amounts|awards)\b|This\s+(?:column|amount|value|figure)\b" +
        @"|Values?\s+(?:in|shown|reported|reflect|represent)\b|Salary\s+(?:equals|amounts?|represents|includes|reflects|for|was|is)\b" +
        @"|Base\s+salary\b|Consists\s+of\b|Includes?\b|Reflects?\b|See\b|Reported\b|Grant\s+date\b|Computed\b|Calculated\b" +
        @"|In\s+accordance\b|Pursuant\s+to\b|Stock\s+awards\b|Option\s+awards\b|All\s+other\s+compensation\b|Non-equity\b" +
        @"|For\s+(?:fiscal|20\d\d|the\s+(?:year|fiscal|period))\b|During\s+(?:fiscal|20\d\d|the\s+(?:year|fiscal))\b" +
        @"|(?:Mr|Ms|Mrs)\.\s+\p{Lu})";

    [GeneratedRegex(@"\s+(?:" + Footnote + @"|Dr\.\s+\p{Lu}[\p{Ll}])")] private static partial Regex FootnoteStart();
    [GeneratedRegex(@"^" + Footnote, RegexOptions.IgnoreCase)] private static partial Regex FootnoteAtStart();
    /// <summary>" Mark Hernandez , 57": two to four capitalised words, a comma and an age.</summary>
    [GeneratedRegex(@"\s+\p{Lu}[\p{Ll}.'’-]+(?:\s+\p{Lu}[\p{Ll}.'’-]*){1,3}\s*,\s*\d{2}\b")] private static partial Regex NameAndAge();
    /// <summary>
    /// The next person's name with a middle initial: " Zachary J. Coughlin", " B. Kevin Turner", " Tucker H. Marshall" — but not a
    /// company named after someone ("T. Marzetti Company").
    /// </summary>
    [GeneratedRegex(@"\s+(?:\p{Lu}\p{Ll}+\s+\p{Lu}\.\s+\p{Lu}[\p{Ll}'’-]+|\p{Lu}\.\s+\p{Lu}\p{Ll}+\s+(?!" + CompanyWords + @")\p{Lu}[\p{Ll}'’-]+)\b(?:\s+(?:Jr\.|Sr\.|II|III|IV)\b)?")]
    private static partial Regex NameWithInitial();
    /// <summary>A name or a year before the title: "James L. Dolan ", "B. Kevin Turner ", "2021 ".</summary>
    [GeneratedRegex(@"^(?:(?:19|20)\d{2}\s+)?(?:\p{Lu}\p{Ll}+\s+\p{Lu}\.\s+\p{Lu}[\p{Ll}'’-]+|\p{Lu}\.\s+\p{Lu}\p{Ll}+\s+(?!" + CompanyWords + @")\p{Lu}[\p{Ll}'’-]+)(?:\s+(?:Jr\.|Sr\.|II|III|IV))?\s+(?=\p{Lu})")]
    private static partial Regex LeadingName();
    private const string CompanyWords = @"(?:Company|Corporation|Corp|Bank|Group|Holdings|Inc|Industries|Partners|Division)\b";
    /// <summary>
    /// Pay amounts from the row that leaked in (" 108,150 — 2,000,011", " (111,306)", " — — —"), dot leaders (" ......") and a
    /// footnote number left at the end ("Former Chief Revenue Officer 1").
    /// </summary>
    [GeneratedRegex(@"\s*\.{4,}|\s+\.{2,}$|\s+(?:\(?\d{1,3}(?:,\d{3})+\)?(?!\w)|[—–](?=\s*(?:[—–]|\d|$))|\d{1,2}$|\$\s?[\d—–-])")] private static partial Regex Amounts();
    /// <summary>". ", ", ", "-" or the age column ("age 59", "age ") before the title.</summary>
    [GeneratedRegex(@"^(?:[.,;:\-–—]+\s*|age\s+(?:\d{2}\b\s*)?|(?:Ph\.?\s?D\.?|M\.?\s?D\.?|J\.?\s?D\.?|CPA|Esq\.?)(?:,\s*|\s+)(?=\p{Lu}))+", RegexOptions.IgnoreCase)] private static partial Regex LeadingDebris();
    /// <summary>A table's section heading left at the end: "… Former Officers", "… Former Employees:".</summary>
    [GeneratedRegex(@"\s+(?:Former|Current|Other)\s+(?:Officers|Employees|Executives|Executive\s+Officers|NEOs)\s*$")] private static partial Regex SectionLabel();
    /// <summary>Any run of spaces, including the non-breaking, thin and zero-width ones filings use for layout.</summary>
    [GeneratedRegex(@"[\s\u00A0\u2000-\u200B\u202F\u205F\u3000\uFEFF]+")] private static partial Regex Spaces();
}
