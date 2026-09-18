using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>A piece of a newly appointed executive's announced package.</summary>
public enum PackagePartKind { Salary, SignOnCash, Bonus, Stock, PerformanceStock, Options, OtherCash }

public sealed record PackagePart(PackagePartKind Kind, decimal Amount, string Label);

/// <summary>An officer appointment read from an 8-K (Item 5.02) with the dollar amounts of their package.</summary>
public sealed record ParsedNewHire(string Name, string Title, DateOnly? StartsOn, IReadOnlyList<PackagePart> Parts)
{
    public decimal Total => Parts.Sum(p => p.Amount);
}

/// <summary>
/// Reads new officer appointments and their pay packages out of an 8-K's Item 5.02 ("Departure of Directors or Certain
/// Officers; … Appointment of Certain Officers; Compensatory Arrangements") with fixed rules, no AI.
/// Deliberately strict: an amount is kept only when the words right next to it say what it is (base salary, sign-on
/// bonus, RSUs…) and nothing near it makes it conditional or someone else's (severance, "up to", monthly fees, another
/// person's terms). A filing it can't read cleanly gives nothing rather than a guess.
/// </summary>
public static partial class NewHireParser
{
    /// <summary>Amounts under this are prices per share, hourly rates and the like, not package items.</summary>
    private const decimal MinAmount = 10_000;

    /// <param name="filed">The filing date: annual awards for a later year ("in 2026 he will also receive…") aren't part of joining.</param>
    public static IReadOnlyList<ParsedNewHire> Parse(string html, DateOnly? filed = null)
    {
        var section = Item502(PlainText(html));
        if (section is null) return [];
        var sentences = Sentences(section);

        // 1. Who was appointed to what. Interim and acting appointments are left out (they're usually paid a monthly fee).
        var hires = new List<(string Name, string Surname, string Title, int Sentence)>();
        for (var i = 0; i < sentences.Count; i++)
            foreach (var (name, title) in Appointments(sentences[i]))
                if (!hires.Any(h => h.Surname == Surname(name)))
                    hires.Add((name, Surname(name), title, i));
        if (hires.Count == 0) return [];

        // Everyone else the section talks about (the person leaving, an interim CEO…), so their terms aren't mixed in.
        var people = hires.Select(h => h.Surname)
            .Concat(Honorific().Matches(section).Select(m => m.Groups["surname"].Value))
            .Distinct(StringComparer.Ordinal).ToList();

        // 2. Walk the sentences, following whose terms they describe: the person named most recently ("he", "his" continue it).
        var parts = hires.ToDictionary(h => h.Surname, _ => new List<PackagePart>());
        var starts = new Dictionary<string, DateOnly?>();
        string? subject = null;
        for (var i = 0; i < sentences.Count; i++)
        {
            var s = sentences[i];
            if (DirectorPay().IsMatch(s)) { subject = null; continue; }   // board fees and retainers, not an officer's package
            var named = people.Select(p => (p, at: IndexOfWord(s, p))).Where(x => x.at >= 0).OrderBy(x => x.at).ToList();
            if (named.Count > 0) subject = named[0].p;
            if (named.Count > 1 && named.Select(x => x.p).Distinct().Count() > 1)
                subject = named.Any(x => parts.ContainsKey(x.p)) && named.Count(x => parts.ContainsKey(x.p)) == 1
                    ? named.First(x => parts.ContainsKey(x.p)).p
                    : null;   // two people's names in one sentence: too ambiguous to attribute amounts
            if (subject is null || !parts.TryGetValue(subject, out var list)) continue;

            starts.TryAdd(subject, null);
            if (starts[subject] is null && StartDate(s) is { } d) starts[subject] = d;
            if (filed is { } fd && IsLaterYearsAward(s, fd)) continue;
            foreach (var part in WithoutTotals(Amounts(s, filed).ToList()))
                if (!list.Any(p => p.Kind == part.Kind && (p.Amount == part.Amount || part.Kind == PackagePartKind.Salary)))
                    list.Add(part);
        }

        return hires
            .Select(h => (h, parts: Sane(parts[h.Surname])))
            .Where(x => x.parts.Count > 0)
            .Select(x => new ParsedNewHire(x.h.Name, x.h.Title, starts.GetValueOrDefault(x.h.Surname), x.parts))
            .ToList();
    }

    /// <summary>
    /// "In 2026, he will also receive an annual grant…": a sentence that opens with a later year and describes annual awards is
    /// about future pay, unless it says the award is granted now ("…will be granted in the fall of 2025").
    /// </summary>
    private static bool IsLaterYearsAward(string sentence, DateOnly filed)
    {
        var opening = LaterYear().Match(sentence);
        if (!opening.Success || opening.Index > 3 || int.Parse(opening.Groups["year"].Value) <= filed.Year || !Annual().IsMatch(sentence)) return false;
        return !GrantedIn().Matches(sentence).Any(m => int.Parse(m.Groups["year"].Value) <= filed.Year);
    }

    /// <summary>
    /// "An initial grant of $6.4M, reflecting $3.875M plus $2.525M" or "an award of $2.75M in the following components: (a)…
    /// (b)… (c)…": a total is followed by its pieces, so an amount equal to the sum of the amounts after it (all of them, or the
    /// next two) is dropped and the pieces kept.
    /// </summary>
    private static List<PackagePart> WithoutTotals(List<PackagePart> parts)
    {
        if (parts.Count < 3) return parts;
        return parts.Where((p, i) =>
        {
            var later = parts.Skip(i + 1).ToList();
            bool Close(decimal sum) => Math.Abs(sum - p.Amount) <= p.Amount * 0.01m;
            return !(later.Count >= 2 && (Close(later.Sum(o => o.Amount)) || Close(later[0].Amount + later[1].Amount)));
        }).ToList();
    }

    /// <summary>
    /// Typos and misreads: no single item above $200M or more than 50 times the base salary, and a "salary" under $50,000 is
    /// a monthly or part-time figure, not an annual salary.
    /// </summary>
    private static List<PackagePart> Sane(List<PackagePart> parts)
    {
        var salary = parts.FirstOrDefault(p => p.Kind == PackagePartKind.Salary)?.Amount;
        return parts.Where(p => p.Amount <= 200_000_000m
                && !(p.Kind == PackagePartKind.Salary && p.Amount < 50_000)
                && !(salary is >= 50_000 && p.Kind != PackagePartKind.Salary && p.Amount > salary * 50))
            .ToList();
    }

    // ---------- Appointments ----------

    private static IEnumerable<(string Name, string Title)> Appointments(string sentence)
    {
        foreach (var re in new[] { VerbThenName(), NameThenVerb(), NameWillServe(), AppointmentOf() })
            foreach (Match m in re.Matches(sentence))
            {
                var name = CleanName(m.Groups["name"].Value);
                var title = CleanTitle(m.Groups["title"].Value);
                if (name is null || title is null) continue;
                yield return (name, title);
            }
    }

    private static string? CleanName(string raw)
    {
        var name = Regex.Replace(raw, @"^(?:Dr|Mr|Ms|Mrs)\.\s+", "").Trim();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 5) return null;
        if (words.Any(w => NotANameWord.Contains(w.TrimEnd(',', '.')))) return null;
        return name;
    }

    private static string? CleanTitle(string raw)
    {
        // "the Company's new …", "Merit's new …", "its next …"
        var title = Regex.Replace(raw.Trim(), @"^(?:the\s+)?(?:(?:Company|[A-Z][\w&.\-]*(?:\s+[A-Z][\w&.\-]*){0,3})['’]s\s+|its\s+|our\s+)?(?:new\s+|next\s+|incoming\s+)?", "").Trim();
        if (title.Length is < 3 or > 90) return null;
        if (!OfficerTitle().IsMatch(title) || Temporary().IsMatch(title)) return null;
        return title;
    }

    private static readonly HashSet<string> NotANameWord = new(StringComparer.Ordinal)
    {
        "The", "Company", "Board", "Directors", "Director", "Chief", "Officer", "President", "Inc", "Corporation", "Corp", "LLC",
        "Committee", "Compensation", "Executive", "Vice", "Senior", "Our", "Its", "His", "Her", "Their", "Effective", "On", "In",
        "As", "Item", "Form", "Agreement", "Employment", "Offer", "Letter", "Plan", "Section", "Exhibit",
    };

    private static string Surname(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.TrimEnd(',', '.')).ToList();
        while (words.Count > 1 && words[^1] is "Jr" or "Sr" or "II" or "III" or "IV") words.RemoveAt(words.Count - 1);
        return words[^1];
    }

    // ---------- Amounts ----------

    private static IEnumerable<PackagePart> Amounts(string sentence, DateOnly? filed)
    {
        foreach (Match m in Money().Matches(sentence))
        {
            var amount = ParseAmount(m);
            if (amount < MinAmount) continue;
            var before = Clause(sentence[Math.Max(0, m.Index - 90)..m.Index]);
            var lead = Clause(sentence[Math.Max(0, m.Index - 160)..m.Index]);
            var after = sentence[(m.Index + m.Length)..Math.Min(sentence.Length, m.Index + m.Length + 70)];
            var near = before.Length > 30 ? before[^30..] : before;

            if (Conditional().IsMatch(near) || Recurring().IsMatch(after[..Math.Min(after.Length, 30)]) || RecurringBefore().IsMatch(before[Math.Max(0, before.Length - 40)..])) continue;
            // Future years' targets ("his target annual equity award will be $7.75M") describe ongoing pay, not the joining package.
            if (Target().IsMatch(lead) && (Annual().IsMatch(lead) || filed is { } fd && Year().Matches(lead).Any(y => int.Parse(y.Value) > fd.Year))) continue;
            if (Severance().IsMatch(before[Math.Max(0, before.Length - 70)..]) || Severance().IsMatch(after[..Math.Min(after.Length, 45)])) continue;
            if (NotPay().IsMatch(before[Math.Max(0, before.Length - 45)..]) || NotPay().IsMatch(after[..Math.Min(after.Length, 25)])) continue;

            if (Classify(before, after) is { } kind)
                yield return new PackagePart(kind.Kind, amount, kind.Label + (Target().IsMatch(before[Math.Max(0, before.Length - 60)..]) && kind.Kind is not PackagePartKind.Salary ? " (target)" : ""));
        }
    }

    /// <summary>Only the clause the amount is in: the text after the last ";" or list marker like "(ii)" before it.</summary>
    private static string Clause(string before)
    {
        var marker = ListMarker().Matches(before).LastOrDefault();
        var cut = Math.Max(before.LastIndexOf(';'), marker is null ? -1 : marker.Index + marker.Length - 1);
        return cut >= 0 ? before[(cut + 1)..] : before;
    }

    /// <summary>The kind of pay whose keyword sits closest to the amount (before it within the clause, or just after it).</summary>
    private static (PackagePartKind Kind, string Label)? Classify(string clause, string after)
    {
        var afterClause = after.Split([';', '('], 2)[0];

        (PackagePartKind, string)? best = null;
        var bestDistance = int.MaxValue;
        foreach (var (re, kind, label) in Kinds)
        {
            var hitBefore = re.Matches(clause).LastOrDefault();
            if (hitBefore is not null && clause.Length - (hitBefore.Index + hitBefore.Length) < bestDistance)
                (best, bestDistance) = ((kind, label), clause.Length - (hitBefore.Index + hitBefore.Length));
            var hitAfter = re.Match(afterClause);
            if (hitAfter.Success && hitAfter.Index < 40 && hitAfter.Index < bestDistance)
                (best, bestDistance) = ((kind, label), hitAfter.Index);
        }
        return best;
    }

    /// <summary>Most specific first: a "sign-on RSU award" is stock, a "sign-on cash bonus" is sign-on cash.</summary>
    private static readonly (Regex Re, PackagePartKind Kind, string Label)[] Kinds =
    [
        (new(@"performance[- ](?:based\s+|vesting\s+)?(?:restricted\s+)?(?:stock|share)|\bP(?:R)?SUs?\b", RegexOptions.IgnoreCase), PackagePartKind.PerformanceStock, "Performance stock"),
        (new(@"stock\s+options?|\boptions?\s+to\s+purchase|\boption\s+(?:award|grant)", RegexOptions.IgnoreCase), PackagePartKind.Options, "Stock options"),
        (new(@"restricted\s+stock|\bRSUs?\b|equity\s+(?:award|grant)|stock\s+(?:award|grant)|inducement\s+(?:award|grant|equity)|long[- ]term\s+incentive|\bLTI\b|grant[- ]date\s+(?:fair\s+)?value", RegexOptions.IgnoreCase), PackagePartKind.Stock, "Stock awards"),
        (new(@"(?:sign[- ]?on|signing|one[- ]time|make[- ]whole|buy[- ]?out|hiring|inducement)(?:\s+(?:cash|bonus|payment|award|amount))*", RegexOptions.IgnoreCase), PackagePartKind.SignOnCash, "Sign-on cash"),
        (new(@"relocation(?:\s+(?:bonus|payment|allowance|benefit))*", RegexOptions.IgnoreCase), PackagePartKind.OtherCash, "Relocation"),
        (new(@"(?:transition|housing|commuting|living)\s+(?:allowance|payment|bonus)", RegexOptions.IgnoreCase), PackagePartKind.OtherCash, "Other cash"),
        (new(@"base\s+salary|annual(?:ized)?\s+salary|\bsalary\b", RegexOptions.IgnoreCase), PackagePartKind.Salary, "Base salary"),
        (new(@"\bbonus", RegexOptions.IgnoreCase), PackagePartKind.Bonus, "Bonus"),
    ];

    private static decimal ParseAmount(Match m)
    {
        var n = decimal.Parse(m.Groups["num"].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        return m.Groups["scale"].Value.ToLowerInvariant() switch
        {
            "million" or "m" or "mm" => n * 1_000_000,
            "billion" or "b" => n * 1_000_000_000,
            "thousand" or "k" => n * 1_000,
            _ => n,
        };
    }

    private static DateOnly? StartDate(string sentence)
    {
        var m = StartsOn().Match(sentence);
        return m.Success && DateOnly.TryParseExact(Regex.Replace(m.Groups["date"].Value, @"\s+", " ").Replace(" ,", ","), ["MMMM d, yyyy", "MMMM dd, yyyy"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ---------- Text ----------

    /// <summary>The filing as plain text: tags dropped, entities decoded, one space between words.</summary>
    public static string PlainText(string html)
    {
        var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(?:/p|/div|br|/tr|/li|/h\d)[^>]*>", " \n ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ').Replace(' ', ' ').Replace(' ', ' ')
            .Replace('“', '"').Replace('”', '"').Replace('’', '\'');
        return Regex.Replace(text, @"[ \t\r\f\v]+", " ");
    }

    /// <summary>From the "Item 5.02" heading to the next item or the signatures.</summary>
    public static string? Item502(string text)
    {
        var start = ItemHeading().Matches(text).FirstOrDefault(m => m.Groups["n"].Value == "5.02");
        if (start is null) return null;
        var from = start.Index + start.Length;
        var end = ItemHeading().Matches(text, from).FirstOrDefault(m => m.Groups["n"].Value != "5.02")?.Index;
        var sig = text.IndexOf("SIGNATURE", from, StringComparison.Ordinal);
        var to = new[] { end ?? text.Length, sig >= 0 ? sig : text.Length }.Min();
        return Regex.Replace(text[from..to], @"\s+", " ").Trim();
    }

    private static List<string> Sentences(string section) =>
        SentenceEnd().Split(section).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static int IndexOfWord(string s, string word)
    {
        var m = Regex.Match(s, $@"\b{Regex.Escape(word)}\b");
        return m.Success ? m.Index : -1;
    }

    // ---------- Patterns ----------

    private const string Name = @"(?<name>(?:(?:Dr|Mr|Ms|Mrs)\.\s+)?[A-Z][A-Za-z'\-]+(?:\s+(?:[A-Z]\.|[A-Z][A-Za-z'\-]+|de|van|von|der|da|del|Jr\.?|III|II)){1,4})";
    private const string Age = @"(?:,\s*(?:age\s*)?\d{2},?)?";
    private const string As = @"\s+(?:as|to\s+serve\s+as|to\s+the\s+(?:position|role|office)\s+of|to\s+the\s+positions?\s+of|to\s+be)\s+";
    private const string Title = @"(?<title>[^.;,()""]{3,90}?)(?=\s*(?:[,.;(""]|\s+e\s?ffective|\s+as\s+of\b|\s+in\s+addition|\s+and\s+(?:entered|executed|signed|a|an|as|the|his|her)\s|\s+of\s+the\s+Company|\s+of\s+[A-Z]|\s+and\s+(?:as\s+)?(?:a\s+)?(?:member|director)|\s+and\s+to\s+|\s+with\s+|\s+commencing|\s+beginning|\s+starting|\s+on\s+[A-Z]|\s+following|\s+upon|\s+until|\s+replacing|\s+succeeding|\s+to\s+succeed|$))";
    private const string Verb = @"(?:appoint(?:ed|s)?|nam(?:ed|es)|elect(?:ed|s)?|promot(?:ed|es)|hir(?:ed|es)|engag(?:ed|es))";

    [GeneratedRegex(Verb + @"\s+" + Name + Age + As + Title)]
    private static partial Regex VerbThenName();

    [GeneratedRegex(Name + Age + @"\s+(?:has\s+been|was|will\s+be|had\s+been|is)\s+" + Verb + As + Title)]
    private static partial Regex NameThenVerb();

    [GeneratedRegex(Name + Age + @"\s+(?:will|to|has\s+agreed\s+to)\s+(?:join\s+(?:the\s+Company\s+)?as|serve\s+as|become)\s+" + Title)]
    private static partial Regex NameWillServe();

    [GeneratedRegex(@"appointment\s+of\s+" + Name + Age + As + Title)]
    private static partial Regex AppointmentOf();

    [GeneratedRegex(@"chief\s+[a-z&,\s]{2,40}?officer|\bC[A-Z]?[EFOTILRMP]O\b|(?<!vice[\s-])\bpresident\b|general\s+counsel|\btreasurer\b|\bcontroller\b|executive\s+vice\s+president|senior\s+vice\s+president", RegexOptions.IgnoreCase)]
    private static partial Regex OfficerTitle();

    [GeneratedRegex(@"\b(?:interim|acting|temporary)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Temporary();

    [GeneratedRegex(@"\b(?:Dr|Mr|Ms|Mrs)\.\s+(?<surname>[A-Z][A-Za-z'\-]+)")]
    private static partial Regex Honorific();

    [GeneratedRegex(@"\$\s?(?<num>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:\s*(?<scale>million|billion|thousand|MM|M|K|B)\b)?", RegexOptions.IgnoreCase)]
    private static partial Regex Money();

    /// <summary>Caps and maximums: "up to $X" isn't what they get.</summary>
    [GeneratedRegex(@"\b(?:up\s+to|maximum(?:\s+of)?|not\s+to\s+exceed|capped\s+at|cap\s+of|no\s+more\s+than|as\s+much\s+as|in\s+excess\s+of)\s*(?:an?\s+)?(?:aggregate\s+)?(?:amount\s+of\s+)?(?:of\s+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Conditional();

    /// <summary>Monthly fees, hourly rates, per-share prices.</summary>
    [GeneratedRegex(@"^\s*(?:per|a|each)\s+(?:month|hour|week|day|share|meeting)|^\s*monthly|^\s*(?:in\s+)?monthly", RegexOptions.IgnoreCase)]
    private static partial Regex Recurring();

    [GeneratedRegex(@"terminat|severance|change\s+(?:in|of)\s+control|good\s+reason|without\s+cause|for\s+cause|separation|death|disability|clawback|repay|forfeit|resign|retire", RegexOptions.IgnoreCase)]
    private static partial Regex Severance();

    [GeneratedRegex(@"exercise\s+price|per\s+share|price\s+of|retainer|director\s+(?:fee|compensation)|non-employee|legal\s+fees|attorney|reimburs|stock\s+price|closing\s+price|market\s+value\s+of|revenue|EBITDA|loan|purchase\s+price|consideration", RegexOptions.IgnoreCase)]
    private static partial Regex NotPay();

    [GeneratedRegex(@"\btarget", RegexOptions.IgnoreCase)]
    private static partial Regex Target();

    [GeneratedRegex(@"\bannual(?:ly)?\b|each\s+(?:fiscal\s+)?year|beginning\s+(?:in|with)", RegexOptions.IgnoreCase)]
    private static partial Regex Annual();

    [GeneratedRegex(@"\b(?:monthly|weekly|hourly|per\s+(?:month|week|hour))\b[^$]*$|product\s+of\s*(?:\(x\)\s*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RecurringBefore();

    [GeneratedRegex(@"\b(?:in|beginning\s+(?:in|with)|starting\s+(?:in|with)|for)\s+(?:the\s+)?(?:(?:fiscal|calendar)\s+(?:year\s+)?)?(?<year>20\d\d)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LaterYear();

    [GeneratedRegex(@"\b20\d\d\b")]
    private static partial Regex Year();

    [GeneratedRegex(@"grant(?:ed)?\s+(?:in|on|during)\s+(?:the\s+)?(?:(?:fall|spring|summer|winter|first|second|third|fourth|quarter|half)\s+(?:quarter\s+)?of\s+)?(?:\w+\s+)?(?<year>20\d\d)", RegexOptions.IgnoreCase)]
    private static partial Regex GrantedIn();

    /// <summary>Sentences about the board's pay (retainers, non-employee director awards).</summary>
    [GeneratedRegex(@"non-employee\s+directors?|each\s+director|director\s+compensation|board\s+(?:fees|retainer)|\bretainer\b", RegexOptions.IgnoreCase)]
    private static partial Regex DirectorPay();

    [GeneratedRegex(@"\(\s*(?:[ivx]+|[a-z]|\d+)\s*\)")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"(?:effective|commencing|beginning|starting)\s+(?:as\s+of\s+|on\s+)?(?<date>(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}\s*,\s*\d{4})")]
    private static partial Regex StartsOn();

    [GeneratedRegex(@"Item\s*(?<n>\d\.\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ItemHeading();

    /// <summary>Sentence ends: ". " before a capital or quote, not after titles and initials (Mr., Inc., Co., a single letter).</summary>
    [GeneratedRegex(@"(?<!\b(?:Mr|Ms|Mrs|Dr|Inc|Co|Corp|Ltd|Jr|Sr|No|St|U\.S|[A-Z]))[.;]\s+(?=[A-Z""(])|\n")]
    private static partial Regex SentenceEnd();
}
