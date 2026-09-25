using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Sec;
using CompanyPaisa.Importer.Uk;

namespace CompanyPaisa.Importer.Anz;

/// <param name="Exchange">"ASX" or "NZX".</param>
/// <param name="Sector">GICS sector or industry (Wikipedia's ASX 200 table, else Wikidata's industry), may be empty.</param>
/// <param name="HqCity">Headquarters city and position from Wikidata, used when no address is found elsewhere.</param>
public sealed record AnzListing(string Exchange, string Ticker, string Name, string Sector, string Website, string Lei, string Wikidata,
    string HqCity, double? HqLatitude, double? HqLongitude)
{
    public string Country => Exchange == "NZX" ? "NZ" : "AU";
}

/// <summary>
/// The ASX and NZX companies to look for, from free sources: Wikidata (CC0) — every company it records as listed on either
/// exchange, with its ticker, website, LEI and headquarters — joined with Wikipedia's S&amp;P/ASX 200 and NZX tables (CC BY-SA),
/// which add companies Wikidata doesn't tie to a ticker and the ASX 200's GICS sectors. Kept as a reviewable CSV.
/// </summary>
public static partial class AnzCompanies
{
    private const string Asx = "http://www.wikidata.org/entity/Q732670", Nzx = "http://www.wikidata.org/entity/Q627019";

    private sealed class Item
    {
        public string Id = "", Name = "", Website = "", Lei = "", Industry = "", HqCity = "", Article = "";
        public double? Lat, Lng;
        public bool Gone;
        public readonly List<(string Exchange, string Ticker, bool Ended)> Listings = [];
    }

    public static async Task<List<AnzListing>> FetchAsync(ISecClient client, AnzOptions o, CancellationToken ct)
    {
        var items = await WikidataAsync(client, o.WikidataSparql, ct);
        var byListing = new Dictionary<(string, string), Item>();
        var byArticle = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Values)
        {
            foreach (var l in item.Listings.Where(l => !l.Ended)) byListing.TryAdd((l.Exchange, l.Ticker), item);
            if (item.Article.Length > 0) byArticle.TryAdd(item.Article, item);
        }

        var result = new Dictionary<(string, string), AnzListing>();
        AnzListing Make(string exchange, string ticker, string name, string sector, Item? i) => new(exchange, ticker,
            i?.Name is { Length: > 0 } n && name.Length == 0 ? n : name.Length > 0 ? name : i?.Name ?? ticker,
            sector.Length > 0 ? sector : i?.Industry ?? "", i?.Website ?? "", i?.Lei ?? "", i?.Id.Split('/')[^1] ?? "", i?.HqCity ?? "", i?.Lat, i?.Lng);

        // Wikipedia's tables first: current members, with names as the index lists them.
        var members = new List<(string Exchange, string Ticker, string Name, string Sector, string Article)>();
        foreach (var page in o.WikipediaPages) members.AddRange(await WikipediaAsync(client, page, ct));
        // Members whose Wikidata item doesn't record the listing: found through their Wikipedia article instead.
        var missing = members.Where(m => m.Article.Length > 0 && !byListing.ContainsKey((m.Exchange, m.Ticker)) && !byArticle.ContainsKey(m.Article))
            .Select(m => m.Article).Distinct().ToList();
        // Table links are often redirects ("Auckland International Airport" → "Auckland Airport"): Wikipedia gives each
        // page's Wikidata item after following them.
        var itemOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // linked title → Q-id
        foreach (var batch in missing.Chunk(50))
        {
            var titles = Uri.EscapeDataString(string.Join("|", batch));
            var answer = await client.GetStringAsync($"https://en.wikipedia.org/w/api.php?action=query&prop=pageprops&ppprop=wikibase_item&redirects=1&format=json&formatversion=2&titles={titles}", CachePolicy.Index, ct);
            if (answer is null) continue;
            using var doc = JsonDocument.Parse(answer);
            var q = doc.RootElement.GetProperty("query");
            var redirects = q.TryGetProperty("redirects", out var r) ? r.EnumerateArray().ToDictionary(x => x.GetProperty("to").GetString()!, x => x.GetProperty("from").GetString()!) : [];
            var normalized = q.TryGetProperty("normalized", out var n) ? n.EnumerateArray().ToDictionary(x => x.GetProperty("to").GetString()!, x => x.GetProperty("from").GetString()!) : [];
            foreach (var page in q.GetProperty("pages").EnumerateArray())
            {
                if (!page.TryGetProperty("pageprops", out var props) || !props.TryGetProperty("wikibase_item", out var qid)) continue;
                var title = page.GetProperty("title").GetString()!;
                var linked = redirects.GetValueOrDefault(title, title);
                itemOf[normalized.GetValueOrDefault(linked, linked)] = qid.GetString()!;
            }
        }
        foreach (var batch in itemOf.Chunk(80))
        {
            var values = string.Join(" ", batch.Select(kv => $"wd:{kv.Value}"));
            var found = await QueryAsync(client, o.WikidataSparql, ArticleQuery.Replace("{items}", values), ct);
            foreach (var (title, qid) in batch)
                if (found.GetValueOrDefault($"http://www.wikidata.org/entity/{qid}") is { } item) byArticle.TryAdd(title, item);
        }
        foreach (var (exchange, ticker, name, sector, article) in members)
        {
            var item = byListing.GetValueOrDefault((exchange, ticker)) ?? (article.Length > 0 ? byArticle.GetValueOrDefault(article) : null);
            result.TryAdd((exchange, ticker), Make(exchange, ticker, name, sector, item));
        }
        // Then every other company Wikidata lists as trading (not delisted, not dissolved) — only those with a website,
        // since the website is where its reports are found.
        foreach (var item in items.Values.Where(i => !i.Gone && i.Website.Length > 0))
            foreach (var l in item.Listings.Where(l => !l.Ended))
                result.TryAdd((l.Exchange, l.Ticker), Make(l.Exchange, l.Ticker, "", "", item));

        // A dual listing (Fletcher Building, Air New Zealand on both): one company, at its home exchange — the NZX for a
        // company headquartered in New Zealand (east of 160°E) or with a ".nz" website.
        return result.Values.GroupBy(l => l.Wikidata.Length > 0 ? l.Wikidata : l.Exchange + l.Ticker)
            .Select(g => g.OrderBy(l => (l.Exchange == "NZX") == InNewZealand(l) ? 0 : 1).First())
            .OrderBy(l => l.Exchange).ThenBy(l => l.Ticker, StringComparer.Ordinal).ToList();

        static bool InNewZealand(AnzListing l) => l.HqLongitude is { } lng ? lng > 160 : l.Website.Contains(".nz", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<Dictionary<string, Item>> WikidataAsync(ISecClient client, string endpoint, CancellationToken ct) =>
        QueryAsync(client, endpoint, ListedQuery, ct);

    private const string ListedQuery = """
            SELECT ?c ?ex ?ticker ?end ?name ?site ?lei ?hqLabel ?coord ?industryLabel ?article ?dissolved WHERE {
              VALUES ?ex { wd:Q732670 wd:Q627019 }
              ?c p:P414 ?s . ?s ps:P414 ?ex .
              OPTIONAL { ?s pq:P249 ?ticker } OPTIONAL { ?s pq:P582 ?end }
              OPTIONAL { ?c rdfs:label ?name FILTER(LANG(?name) = "en") }
              OPTIONAL { ?c wdt:P856 ?site } OPTIONAL { ?c wdt:P1278 ?lei }
              OPTIONAL { ?c wdt:P576 ?dissolved }
              OPTIONAL { ?c wdt:P159 ?hq . ?hq rdfs:label ?hqLabel FILTER(LANG(?hqLabel) = "en") OPTIONAL { ?hq wdt:P625 ?coord } }
              OPTIONAL { ?c wdt:P452 ?industry . ?industry rdfs:label ?industryLabel FILTER(LANG(?industryLabel) = "en") }
              OPTIONAL { ?article schema:about ?c ; schema:isPartOf <https://en.wikipedia.org/> }
            }
            """;

    /// <summary>The same details for given Wikidata items ({items} = "wd:Q1 wd:Q2 …").</summary>
    private const string ArticleQuery = """
            SELECT ?c ?name ?site ?lei ?hqLabel ?coord ?industryLabel ?dissolved WHERE {
              VALUES ?c { {items} }
              OPTIONAL { ?c rdfs:label ?name FILTER(LANG(?name) = "en") }
              OPTIONAL { ?c wdt:P856 ?site } OPTIONAL { ?c wdt:P1278 ?lei }
              OPTIONAL { ?c wdt:P576 ?dissolved }
              OPTIONAL { ?c wdt:P159 ?hq . ?hq rdfs:label ?hqLabel FILTER(LANG(?hqLabel) = "en") OPTIONAL { ?hq wdt:P625 ?coord } }
              OPTIONAL { ?c wdt:P452 ?industry . ?industry rdfs:label ?industryLabel FILTER(LANG(?industryLabel) = "en") }
            }
            """;

    private static async Task<Dictionary<string, Item>> QueryAsync(ISecClient client, string endpoint, string sparql, CancellationToken ct)
    {
        var json = await client.GetStringAsync($"{endpoint}?format=json&query={Uri.EscapeDataString(sparql)}", CachePolicy.Index, ct)
                   ?? throw new InvalidOperationException("Wikidata didn't answer.");
        using var doc = JsonDocument.Parse(json);
        var items = new Dictionary<string, Item>();
        foreach (var b in doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
        {
            string V(string name) => b.TryGetProperty(name, out var p) ? p.GetProperty("value").GetString() ?? "" : "";
            var id = V("c");
            if (!items.TryGetValue(id, out var item)) items[id] = item = new Item { Id = id };
            var exchange = V("ex") == Asx ? "ASX" : V("ex") == Nzx ? "NZX" : "";
            var ticker = Ticker(V("ticker"));
            if (exchange.Length > 0 && ticker.Length > 0 && !item.Listings.Any(l => l.Exchange == exchange && l.Ticker == ticker))
                item.Listings.Add((exchange, ticker, V("end").Length > 0));
            else if (V("end").Length > 0)
                for (var i = 0; i < item.Listings.Count; i++) if (item.Listings[i].Exchange == exchange && item.Listings[i].Ticker == ticker) item.Listings[i] = item.Listings[i] with { Ended = true };
            if (item.Name.Length == 0) item.Name = V("name");
            // Several websites: an Australian or New Zealand one (a2 Milk also lists its Chinese site), then the shortest
            // (the main site, not a campaign or investor subdomain).
            var site = Enrichment.WebsiteFinder.Tidy(V("site")) is not null ? V("site") : "";
            if (site.Length > 0 && (item.Website.Length == 0 || (Local(site), -site.Length).CompareTo((Local(item.Website), -item.Website.Length)) > 0)) item.Website = site;
            if (item.Lei.Length == 0) item.Lei = V("lei");
            if (item.Industry.Length == 0) item.Industry = V("industryLabel");
            if (V("dissolved").Length > 0) item.Gone = true;
            if (item.HqCity.Length == 0 && V("hqLabel").Length > 0)
            {
                item.HqCity = V("hqLabel");
                // "Point(151.2093 -33.8688)": longitude first.
                if (CoordPattern().Match(V("coord")) is { Success: true } m)
                {
                    item.Lng = double.Parse(m.Groups["lng"].Value, CultureInfo.InvariantCulture);
                    item.Lat = double.Parse(m.Groups["lat"].Value, CultureInfo.InvariantCulture);
                }
            }
            if (item.Article.Length == 0 && V("article").Length > 0) item.Article = Uri.UnescapeDataString(V("article").Split("/wiki/")[^1]).Replace('_', ' ');
        }
        return items;
    }

    /// <summary>1 for a website in Australia or New Zealand (or a plain .com), 0 for another country's.</summary>
    private static int Local(string url) =>
        Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out var u) &&
        (u.Host.EndsWith(".au", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".nz", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".com", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

    /// <summary>"ASX:BHP", "BHP.AX", "bhp" → "BHP".</summary>
    private static string Ticker(string raw)
    {
        var t = raw.Trim().ToUpperInvariant();
        if (t.Contains(':')) t = t[(t.LastIndexOf(':') + 1)..];
        if (t.EndsWith(".AX", StringComparison.Ordinal) || t.EndsWith(".NZ", StringComparison.Ordinal)) t = t[..^3];
        return TickerPattern().IsMatch(t) ? t : "";
    }

    /// <summary>
    /// The page's table of companies: a header naming the code ("Code", "Symbol", "Ticker") and the company ("Company",
    /// "Stock name"); the first such table only (the NZX page's later tables are delisted companies).
    /// </summary>
    private static async Task<List<(string Exchange, string Ticker, string Name, string Sector, string Article)>> WikipediaAsync(ISecClient client, string page, CancellationToken ct)
    {
        var json = await client.GetStringAsync($"https://en.wikipedia.org/w/api.php?action=parse&page={page}&prop=text&format=json&formatversion=2", CachePolicy.Index, ct)
                   ?? throw new InvalidOperationException($"Couldn't read Wikipedia page {page}.");
        using var doc = JsonDocument.Parse(json);
        var html = doc.RootElement.GetProperty("parse").GetProperty("text").GetString() ?? "";
        var exchange = page.Contains("Zealand", StringComparison.OrdinalIgnoreCase) || page.Contains("NZX", StringComparison.OrdinalIgnoreCase) ? "NZX" : "ASX";
        var result = new List<(string, string, string, string, string)>();
        foreach (Match table in WikiTable().Matches(html))
        {
            var rows = RowPattern().Matches(table.Value).Select(r => CellPattern().Matches(r.Value).Select(c => c.Groups[1].Value).ToList()).ToList();
            if (rows.Count < 2) continue;
            var header = rows[0].Select(Clean).ToList();
            var code = header.FindIndex(h => CodeHeader().IsMatch(h));
            var name = header.FindIndex(h => NameHeader().IsMatch(h));
            if (code < 0 || name < 0) continue;
            var sector = header.FindIndex(h => h.StartsWith("Sector", StringComparison.OrdinalIgnoreCase) || h.StartsWith("Industry", StringComparison.OrdinalIgnoreCase));
            foreach (var r in rows.Skip(1).Where(r => r.Count > Math.Max(code, name)))
            {
                var ticker = Ticker(Clean(r[code]).Split(' ', ':').Last(p => p.Length > 0));
                // Funds, ETFs, warrants and bonds aren't operating companies.
                if (ticker.Length == 0 || NotACompany().IsMatch(Clean(r[name]))) continue;
                var article = ArticleLink().Match(r[name]) is { Success: true } a ? Uri.UnescapeDataString(a.Groups[1].Value).Replace('_', ' ') : "";
                result.Add((exchange, ticker, Clean(r[name]), sector >= 0 && sector < r.Count ? Clean(r[sector]) : "", article));
            }
            break;
        }
        return result;
    }

    public static List<AnzListing> Read(string path)
    {
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(Csv.Split).Where(f => f.Count >= 10)
            .Select(f => new AnzListing(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], Number(f[8]), Number(f[9]))).ToList();

        static double? Number(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static void Write(string path, IEnumerable<AnzListing> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, rows.Select(r => string.Join(',', Csv.Quote(r.Exchange), Csv.Quote(r.Ticker), Csv.Quote(r.Name), Csv.Quote(r.Sector),
                Csv.Quote(r.Website), Csv.Quote(r.Lei), Csv.Quote(r.Wikidata), Csv.Quote(r.HqCity),
                r.HqLatitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "", r.HqLongitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? ""))
            .Prepend("exchange,ticker,name,sector,website,lei,wikidata,hq_city,hq_latitude,hq_longitude"), new UTF8Encoding(false));
    }

    private static string Clean(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<sup[\\s\\S]*?</sup>", ""), "<[^>]+>", " ")), @"\s+", " ").Trim();

    [GeneratedRegex(@"<table[^>]*wikitable[\s\S]*?</table>")] private static partial Regex WikiTable();
    [GeneratedRegex(@"<tr[\s\S]*?</tr>")] private static partial Regex RowPattern();
    [GeneratedRegex(@"<t[hd][^>]*>([\s\S]*?)</t[hd]>")] private static partial Regex CellPattern();
    [GeneratedRegex(@"<a[^>]*href=""/wiki/([^""#]+)""")] private static partial Regex ArticleLink();
    [GeneratedRegex(@"^(code|symbol|ticker|asx\s+code|ex\s+symbol)", RegexOptions.IgnoreCase)] private static partial Regex CodeHeader();
    [GeneratedRegex(@"^(company|stock\s+name|name)", RegexOptions.IgnoreCase)] private static partial Regex NameHeader();
    [GeneratedRegex(@"^[A-Z0-9]{2,6}$")] private static partial Regex TickerPattern();
    [GeneratedRegex(@"\bETF\b|smartshares|\bwarrants?\b|\bfund\b|\bbonds?\b|\bnotes\b|\boptions\b|\bconvertible\b|\bpreference\s+shares\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotACompany();
    [GeneratedRegex(@"Point\((?<lng>-?[\d.]+)\s+(?<lat>-?[\d.]+)\)")] private static partial Regex CoordPattern();
}
