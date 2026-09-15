using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>The CEO's ("principal executive officer's") total pay for one fiscal year, as tagged by the company.</summary>
/// <param name="FiscalYear">Year label as proxies use it (a year ending in January or February belongs to the year before).</param>
/// <param name="Name">The PEO's name when the filing tags it (needed when two people held the job in one year).</param>
public sealed record PvpFact(int FiscalYear, DateOnly PeriodEnd, decimal Total, string? Name);

/// <summary>
/// Reads the pay-versus-performance table's machine-readable tags from a proxy statement (inline XBRL, required since
/// 2023 and covering up to five years back). <c>ecd:PeoTotalCompAmt</c> is by rule the same figure as the CEO's
/// "Total" in the summary compensation table, so it is an independent check on what the table parser read.
/// </summary>
public static partial class PayVersusPerformance
{
    public static IReadOnlyList<PvpFact> Read(string html)
    {
        if (!html.Contains("ecd:PeoTotalCompAmt", StringComparison.Ordinal)) return [];

        var contexts = new Dictionary<string, (DateOnly End, string? Person)>(StringComparer.Ordinal);
        foreach (Match m in Context().Matches(html))
        {
            if (EndDate().Match(m.Groups["body"].Value) is not { Success: true } e ||
                !DateOnly.TryParse(e.Groups[1].Value, CultureInfo.InvariantCulture, out var end)) continue;
            var person = Individual().Match(m.Groups["body"].Value);
            contexts[m.Groups["id"].Value] = (end, person.Success ? person.Groups[1].Value.Trim() : null);
        }

        // Names: per (period end, individual member) — a single-CEO filing tags the name with a member but the total without.
        var names = new List<(DateOnly End, string? Person, string Name)>();
        foreach (Match m in PeoName().Matches(html))
        {
            if (Attr(m.Groups["attrs"].Value, "contextRef") is not { } ctx || !contexts.TryGetValue(ctx, out var c)) continue;
            var name = CleanName(m.Groups["value"].Value);
            if (name.Length > 0) names.Add((c.End, c.Person, name));
        }

        var facts = new List<PvpFact>();
        foreach (Match m in PeoTotal().Matches(html))
        {
            var attrs = m.Groups["attrs"].Value;
            if (Attr(attrs, "contextRef") is not { } ctx || !contexts.TryGetValue(ctx, out var c)) continue;
            if (ParseAmount(m.Groups["value"].Value, attrs) is not { } total) continue;
            var candidates = names.Where(n => n.End == c.End && (c.Person is null || n.Person == c.Person)).Select(n => n.Name).Distinct().ToList();
            facts.Add(new PvpFact(c.End.Month <= 2 ? c.End.Year - 1 : c.End.Year, c.End, total, candidates.Count == 1 ? candidates[0] : null));
        }
        return facts.DistinctBy(f => (f.PeriodEnd, f.Total, f.Name)).ToList();
    }

    private static decimal? ParseAmount(string inner, string attrs)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(inner, "")).Trim();
        decimal value;
        if (text is "" or "-" or "—" or "–" || (Attr(attrs, "format") ?? "").Contains("zero", StringComparison.OrdinalIgnoreCase)) value = 0;
        else if (!decimal.TryParse(text.Replace(",", "").Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return null;
        if (int.TryParse(Attr(attrs, "scale"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale) && scale != 0)
            value *= (decimal)Math.Pow(10, scale);
        return Attr(attrs, "sign") == "-" ? -value : value;
    }

    /// <summary>"Mr. Cook" → "Cook"; titles and markup removed.</summary>
    private static string CleanName(string inner)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(inner, " "));
        text = Honorific().Replace(Regex.Replace(text, @"\s+", " "), "").Trim();
        return text.Length > 80 ? "" : text;
    }

    private static string? Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, $@"\b{name}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Does a PvP name ("Cook", "Tim Cook", "Timothy D. Cook") refer to this table name?</summary>
    public static bool SamePerson(string pvpName, string tableName)
    {
        static string[] Words(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z\s]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && w is not ("jr" or "sr" or "ii" or "iii" or "iv")).ToArray();
        var a = Words(pvpName);
        var b = Words(tableName);
        return a.Length > 0 && b.Length > 0 && a[^1] == b[^1];
    }

    [GeneratedRegex(@"<(?:xbrli:)?context\b[^>]*\bid=""(?<id>[^""]+)""[^>]*>(?<body>.*?)</(?:xbrli:)?context>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Context();
    [GeneratedRegex(@"<(?:xbrli:)?endDate>\s*([0-9-]{10})\s*<", RegexOptions.IgnoreCase)] private static partial Regex EndDate();
    [GeneratedRegex(@"dimension=""ecd:IndividualAxis""\s*>([^<]+)<", RegexOptions.IgnoreCase)] private static partial Regex Individual();
    [GeneratedRegex(@"<ix:nonFraction\b(?<attrs>[^>]*\bname=""ecd:PeoTotalCompAmt""[^>]*)>(?<value>.*?)</ix:nonFraction>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PeoTotal();
    [GeneratedRegex(@"<ix:nonNumeric\b(?<attrs>[^>]*\bname=""ecd:PeoName""[^>]*)>(?<value>.*?)</ix:nonNumeric>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PeoName();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"^(mr|mrs|ms|miss|dr)\.?\s+", RegexOptions.IgnoreCase)] private static partial Regex Honorific();
}
