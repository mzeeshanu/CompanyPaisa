using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Sec;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>
/// A company's own website, from free sources: Wikidata's "official website" (CC0), matched by SEC number (CIK), legal
/// entity identifier (LEI) or exchange ticker; and, for SEC filers Wikidata doesn't have, the website the company names in
/// its own proxy statement or annual report ("available on our website at www.example.com") — accepted only when the
/// domain looks like the company's name or ticker.
/// </summary>
public sealed partial class WebsiteFinder(ISecClient wikidata, string sparqlEndpoint)
{
    private Dictionary<string, List<string>> _byCik = new(), _byLei = new(), _byTicker = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Exchanges as Wikidata names them → the ticker form our ids use.</summary>
    private static readonly Dictionary<string, string> Exchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New York Stock Exchange"] = "", ["Nasdaq"] = "", ["NYSE American"] = "", ["Toronto Stock Exchange"] = "",
        ["London Stock Exchange"] = ".L", ["Euronext Paris"] = ".PA", ["Euronext Amsterdam"] = ".AS",
        ["Borsa Italiana"] = ".MI", ["Italian Stock Exchange"] = ".MI", ["Madrid Stock Exchange"] = ".MC", ["Bolsa de Madrid"] = ".MC",
        ["Pakistan Stock Exchange"] = ".KA"
    };

    public async Task LoadAsync(CancellationToken ct)
    {
        _byCik = Normalise(await QueryAsync("SELECT ?id ?site WHERE { ?c wdt:P5531 ?id ; wdt:P856 ?site . }", ct), id => id.TrimStart('0'));
        _byLei = Normalise(await QueryAsync("SELECT ?id ?site WHERE { ?c wdt:P1278 ?id ; wdt:P856 ?site . }", ct), id => id.ToUpperInvariant());
        var tickers = await QueryAsync("""
            SELECT ?id ?exch ?site WHERE { ?c p:P414 ?st . ?st ps:P414 ?e ; pq:P249 ?id . ?e rdfs:label ?exch .
              FILTER(LANG(?exch) = "en") ?c wdt:P856 ?site . }
            """, ct);
        _byTicker = new(StringComparer.OrdinalIgnoreCase);
        foreach (var row in tickers)
            if (Exchanges.TryGetValue(row.GetValueOrDefault("exch") ?? "", out var suffix))
                Add(_byTicker, row["id"].ToUpperInvariant() + suffix, row["site"]);
    }

    public int Known => _byCik.Count + _byLei.Count + _byTicker.Count;

    /// <summary>The website Wikidata gives for the company, or null.</summary>
    public string? FromWikidata(long? cik, string? lei, string ticker) =>
        Best(cik is { } c ? _byCik.GetValueOrDefault(c.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null)
        ?? Best(lei is null ? null : _byLei.GetValueOrDefault(lei.ToUpperInvariant()))
        ?? Best(_byTicker.GetValueOrDefault(ticker.ToUpperInvariant()));

    /// <summary>The company's domain as named in its own filing text, when it matches its name or ticker.</summary>
    public static string? FromFilingText(string html, string name, string ticker)
    {
        var text = WebUtilityDecode(TagPattern().Replace(html, " "));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in DomainPattern().Matches(text))
        {
            var host = m.Groups["host"].Value.TrimEnd('.').ToLowerInvariant();
            var domain = Registrable(host);
            if (domain is null || NotTheCompany().IsMatch(domain)) continue;
            counts[domain] = counts.GetValueOrDefault(domain) + 1;
        }
        return counts.Where(kv => LooksLike(kv.Key, name, ticker)).OrderByDescending(kv => kv.Value).Select(kv => "https://www." + kv.Key).FirstOrDefault();
    }

    /// <summary>"https://ir.53.com/investors" → "https://www.53.com": the company's home page, not investor relations.</summary>
    public static string? Tidy(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (!u.Contains("://", StringComparison.Ordinal)) u = "https://" + u;
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return null;
        var host = uri.Host.ToLowerInvariant();
        if (InvestorHost().Match(host) is { Success: true } m) host = "www." + host[m.Length..];
        return $"{(uri.Scheme == "http" ? "http" : "https")}://{host}";
    }

    /// <summary>"lucky-cement" for Lucky Cement, "53" or "fifththird" for Fifth Third (ticker FITB), "tsco" for Tesco (TSCO).</summary>
    internal static bool LooksLike(string domain, string name, string ticker)
    {
        var label = domain.Split('.')[0].Replace("-", "");
        var t = ticker.Split('.')[0].ToLowerInvariant();
        if (label == t || label.StartsWith(t, StringComparison.Ordinal) && t.Length >= 3) return true;
        var words = NameWords().Matches(name.ToLowerInvariant()).Select(w => w.Value).Where(w => !CorporateWords.Contains(w)).ToList();
        if (words.Count == 0) return false;
        var joined = string.Concat(words);
        if (label.Length >= 4 && (joined.StartsWith(label, StringComparison.Ordinal) || label.StartsWith(joined, StringComparison.Ordinal))) return true;
        if (words[0].Length >= 4 && label.StartsWith(words[0], StringComparison.Ordinal)) return true;
        var initials = string.Concat(words.Select(w => w[0]));
        return initials.Length >= 3 && label == initials;
    }

    private static readonly HashSet<string> CorporateWords =
        ["the", "inc", "corp", "corporation", "company", "co", "ltd", "limited", "plc", "holdings", "holding", "group", "sa", "se", "nv", "spa", "of", "and"];

    private async Task<List<Dictionary<string, string>>> QueryAsync(string sparql, CancellationToken ct)
    {
        var url = $"{sparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparql)}";
        var json = await wikidata.GetStringAsync(url, CachePolicy.Index, ct) ?? throw new InvalidOperationException("Wikidata didn't answer.");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray()
            .Select(b => b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetProperty("value").GetString() ?? ""))
            .ToList();
    }

    private static Dictionary<string, List<string>> Normalise(List<Dictionary<string, string>> rows, Func<string, string> key)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) Add(map, key(r["id"]), r["site"]);
        return map;
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string site)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(site);
    }

    /// <summary>Of several listed sites, the company's main one: not an investor-relations host, shortest.</summary>
    private static string? Best(List<string>? sites) =>
        sites?.Select(s => (Raw: s, Tidy: Tidy(s))).Where(x => x.Tidy is not null)
            .OrderBy(x => InvestorHost().IsMatch(new Uri(x.Raw.Contains("://") ? x.Raw : "https://" + x.Raw).Host) ? 1 : 0)
            .ThenBy(x => x.Tidy!.Length).Select(x => x.Tidy).FirstOrDefault();

    /// <summary>"investor.apple.com" → "apple.com"; "tesco.co.uk" keeps its three labels.</summary>
    private static string? Registrable(string host)
    {
        var parts = host.Split('.');
        if (parts.Length < 2) return null;
        var twoLevel = parts.Length >= 3 && parts[^2] is "co" or "com" or "org" or "net" or "gov" or "ac" && parts[^1].Length == 2;
        return string.Join('.', parts[^(twoLevel ? 3 : 2)..]);
    }

    private static string WebUtilityDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagPattern();
    [GeneratedRegex(@"(?:https?://|\bwww\.)(?<host>(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();
    [GeneratedRegex(@"^(ir|investors?|investor-relations|corporate)\.", RegexOptions.IgnoreCase)] private static partial Regex InvestorHost();
    [GeneratedRegex(@"[a-z0-9]+")] private static partial Regex NameWords();
    /// <summary>Regulators, filing agents, transfer agents, voting services and standards bodies named in every filing.</summary>
    [GeneratedRegex(@"^(sec|investor|proxyvote|broadridge|computershare|equiniti|astfinancial|eqs|fasb|pcaobus|xbrl|nasdaq|nyse|edgar-online|virtualshareholdermeeting|meetnow|envisionreports|proxydocs|viewproxy|cstproxy|issuerdirect|investorvote|irs|federalreserve|fdic|finra|sipc|w3|eqsgroup|sedar|sedarplus)\.|\.gov$|\.gov\.[a-z]{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheCompany();
}
