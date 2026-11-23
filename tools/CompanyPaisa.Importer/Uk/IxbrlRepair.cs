using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CompanyPaisa.Importer.Uk;

/// <summary>
/// filings.xbrl.org's xBRL-JSON gives "(ixTransformValueError)" instead of a number when a report writes its figures in a
/// way the inline-XBRL format rules don't allow — Investor's 2023 and 2024 reports put a narrow no-break space and a space
/// together as the thousands separator ("59 643"), which lost every 2023 figure. The report's own HTML still has the
/// figure, under the same fact ID the JSON uses, so each failed value is read from there instead.
/// </summary>
public static partial class IxbrlRepair
{
    public const string Failed = "(ixTransformValueError)";

    public static bool NeedsRepair(string json) => json.Contains(Failed, StringComparison.Ordinal);

    /// <summary>The JSON with every failed value the HTML can supply filled in; how many were filled.</summary>
    public static (string Json, int Repaired) Repair(string json, string html)
    {
        var values = Values(html);
        if (values.Count == 0 || JsonNode.Parse(json) is not JsonObject root || root["facts"] is not JsonObject facts) return (json, 0);
        var repaired = 0;
        foreach (var (id, node) in facts)
        {
            if (node is not JsonObject fact || fact["value"]?.GetValue<string>() != Failed) continue;
            if (!values.TryGetValue(id, out var v)) continue;
            fact["value"] = v.ToString(CultureInfo.InvariantCulture);
            repaired++;
        }
        return (repaired == 0 ? json : root.ToJsonString(), repaired);
    }

    /// <summary>Every numeric fact in the inline-XBRL HTML that has an ID: ID → value (scaled and signed).</summary>
    internal static Dictionary<string, decimal> Values(string html)
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (Match m in NonFraction().Matches(html))
        {
            var attributes = Attributes(m.Groups["attrs"].Value);
            if (!attributes.TryGetValue("id", out var id)) continue;
            if (Number(m.Groups["text"].Value, attributes.GetValueOrDefault("format") ?? "") is not { } n) continue;
            if (attributes.TryGetValue("scale", out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale) && scale is >= -10 and <= 12)
                n *= (decimal)Math.Pow(10, scale);
            if (attributes.GetValueOrDefault("sign") == "-") n = -n;
            values.TryAdd(id, n);
        }
        return values;
    }

    /// <summary>The displayed number, read leniently: any kind of space is a thousands separator.</summary>
    internal static decimal? Number(string inner, string format)
    {
        var text = WebUtility.HtmlDecode(Tag().Replace(inner, ""));
        var f = format.ToLowerInvariant();
        if (f.Contains("zero") || f.Contains("nocontent") || text.Trim() is "-" or "–" or "—") return 0;
        var sb = new StringBuilder();
        foreach (var c in text) if (!char.IsWhiteSpace(c)) sb.Append(c);
        var t = sb.ToString();
        if (f.Contains("comma-decimal") || f.Contains("numcommadecimal") || f.Contains("num-comma-decimal")) t = t.Replace(".", "").Replace(',', '.');
        else if (f.Contains("dot-decimal") || f.Contains("numdotdecimal") || f.Length == 0) t = t.Replace(",", "");
        else return null;
        return NumberText().IsMatch(t) && decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in Attribute().Matches(text)) d.TryAdd(a.Groups["name"].Value, WebUtility.HtmlDecode(a.Groups["value"].Value));
        return d;
    }

    // A nested nonFraction (rare) makes the outer one unreadable; it is then skipped, not misread.
    [GeneratedRegex(@"<ix:nonFraction\b(?<attrs>[^>]*)>(?<text>(?:(?!<ix:nonFraction\b).)*?)</ix:nonFraction>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NonFraction();
    [GeneratedRegex(@"(?<name>[\w:.-]+)\s*=\s*""(?<value>[^""]*)""")] private static partial Regex Attribute();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex(@"^\d+(\.\d+)?$")] private static partial Regex NumberText();
}
