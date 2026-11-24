using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>
/// Reads the category each company's exchange publishes for its listing — the category it actually goes by:
/// <list type="bullet">
/// <item>London: the LSE's monthly instrument list (ICB industry and super-sector, by TIDM).</item>
/// <item>Stockholm, Copenhagen, Helsinki: Nasdaq Nordic's share lists and each share's summary (ICB code and super-sector).</item>
/// <item>Vilnius, Riga, Tallinn: Nasdaq Baltic's share list (ICB industry and super-sector).</item>
/// <item>Paris, Amsterdam, Brussels, Oslo, Milan: Euronext's factsheet for the share (all four ICB levels).</item>
/// <item>ASX: the ASX's list of listed companies (GICS industry group).</item>
/// <item>Pakistan: the PSX sector the import already read.</item>
/// </list>
/// US filers get the SEC's industry code from the SEC import itself. Exchanges not listed here are left unclassified and
/// show up in the audit.
/// </summary>
public sealed partial class CategoryFinder(ISecClient client, string euCacheDirectory, ILogger log)
{
    public const string LseInstruments = "https://docs.londonstockexchange.com/reports/instruments/Instrument%20list.xlsx";
    public const string NasdaqNordicList = "https://api.nasdaq.com/api/nordic/screener/shares?category={1}&tableonly=false&market={0}";
    public const string NasdaqNordicSummary = "https://api.nasdaq.com/api/nordic/instruments/{0}/summary?assetClass=SHARES";
    public const string NasdaqBalticList = "https://nasdaqbaltic.com/statistics/en/shares?download=1";
    public const string EuronextList = "https://live.euronext.com/en/pd_es/data/stocks/download?mics=dm_all_stock&args%5Bfe_type%5D=csv&args%5Bfe_decimal_separator%5D=.&args%5Bfe_date_format%5D=d%2Fm%2FY";
    public const string EuronextIcb = "https://live.euronext.com/en/ajax/getFactsheetInfoBlock/STOCK/{0}-{1}/fs_icb_block";
    public const string BmeCompanies = "https://apiweb.bolsasymercados.es/Market/v1/EQ/ListedCompanies?ISIN=&sectorKey=&subsectorKey=&tradingSystem=SIBE&mtfSegment=&page=0&pageSize=0";
    public const string BmeSectors = "https://apiweb.bolsasymercados.es/Market/v1/EQ/SubsectorsAll?language=en";
    public const string AsxList = "https://www.asx.com.au/asx/research/ASXListedCompanies.csv";

    private static readonly Dictionary<string, (string Market, string Suffix)> Nordic = new()
    {
        ["Nasdaq Stockholm"] = ("STO", ".ST"), ["Nasdaq Copenhagen"] = ("CPH", ".CO"), ["Nasdaq Helsinki"] = ("HEL", ".HE")
    };

    /// <summary>Euronext market codes (MICs) to try for a company, its own market's first.</summary>
    private static readonly Dictionary<string, string[]> EuronextMics = new()
    {
        ["Euronext Paris"] = ["XPAR", "ALXP"], ["Euronext Amsterdam"] = ["XAMS"], ["Euronext Brussels"] = ["XBRU", "ALXB"],
        ["Oslo Børs"] = ["XOSL", "XOAS", "MERK"], ["Borsa Italiana"] = ["XMIL", "EXGM"]
    };
    private static readonly string[] AnyEuronext = ["XPAR", "XAMS", "XBRU", "XOSL", "XMIL", "XLIS", "XMSM"];

    /// <summary>The city in Euronext's market names ("Euronext Growth Paris", "Oslo Børs") for each of our exchanges.</summary>
    private static readonly Dictionary<string, string> EuronextCity = new()
    {
        ["Euronext Paris"] = "Paris", ["Euronext Amsterdam"] = "Amsterdam", ["Euronext Brussels"] = "Brussels", ["Oslo Børs"] = "Oslo", ["Borsa Italiana"] = "Milan"
    };

    /// <summary>Euronext's market name → its MIC, for the factsheet address.</summary>
    internal static string? MicOf(string market) => market.Trim() switch
    {
        "Euronext Paris" => "XPAR", "Euronext Growth Paris" => "ALXP", "Euronext Access Paris" => "XMLI",
        "Euronext Amsterdam" => "XAMS", "Euronext Brussels" => "XBRU", "Euronext Growth Brussels" => "ALXB",
        "Oslo Børs" => "XOSL", "Euronext Growth Oslo" => "XOAS", "Euronext Expand Oslo" => "MERK",
        "Euronext Milan" => "XMIL", "Euronext Growth Milan" => "EXGM", "Euronext Lisbon" => "XLIS", "Euronext Dublin" => "XMSM",
        _ => null
    };

    /// <param name="pakistanAsOf">When the Pakistan import read the PSX sectors (its data date; Pakistan is refreshed separately).</param>
    public async Task<Dictionary<string, IndustryClassification>> FindAsync(IReadOnlyList<Company> companies, DateOnly today,
        List<string> notes, CancellationToken ct, DateOnly? pakistanAsOf = null)
    {
        var found = new Dictionary<string, IndustryClassification>(StringComparer.OrdinalIgnoreCase);
        var byExchange = companies.ToLookup(c => c.Exchange, StringComparer.OrdinalIgnoreCase);
        var missed = new List<string>();

        await LondonAsync(byExchange["LSE"].ToList(), found, missed, today, notes, ct);
        foreach (var (exchange, (market, suffix)) in Nordic)
            await NordicAsync(exchange, market, suffix, byExchange[exchange].ToList(), found, missed, today, ct);
        await BalticAsync(companies.Where(c => c.Exchange.StartsWith("Nasdaq ", StringComparison.Ordinal) && !Nordic.ContainsKey(c.Exchange)).ToList(),
            found, missed, today, ct);
        await EuronextAsync(companies.Where(c => EuronextMics.ContainsKey(c.Exchange)).ToList(), found, missed, today, ct);
        await AsxAsync(byExchange["ASX"].ToList(), found, missed, today, ct);
        await MadridAsync(byExchange["Bolsa de Madrid"].ToList(), found, missed, today, ct);
        foreach (var c in companies.Where(c => c.Market == "pk" && !string.IsNullOrWhiteSpace(c.Industry)))
            found[c.CompanyId] = new IndustryClassification
            {
                Scheme = "PSX", Name = c.Industry!, Source = "Pakistan Stock Exchange", CheckedOn = pakistanAsOf ?? today
            };

        if (missed.Count > 0) notes.Add($"Not on the exchange's list ({missed.Count}): {string.Join(", ", missed.Order())}.");
        return found;
    }

    // ---- London ----------------------------------------------------------------------------------------------------

    private async Task LondonAsync(List<Company> companies, Dictionary<string, IndustryClassification> found, List<string> missed,
        DateOnly today, List<string> notes, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var bytes = await client.GetBytesAsync(LseInstruments, CachePolicy.Index, ct);
        if (bytes is null) { log.LogWarning("LSE instrument list unavailable; London categories kept from the last run"); return; }
        var (rows, asOf) = ReadLse(bytes);
        notes.Add($"London Stock Exchange instrument list as at {asOf?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) ?? "?"}: {rows.Count} shares.");
        foreach (var c in companies)
        {
            var tidm = Strip(c.Ticker, ".L");
            // Two-letter codes carry a trailing dot on the LSE's list ("BP.", "AV.").
            if (!rows.TryGetValue(tidm, out var r) && !rows.TryGetValue(tidm.Replace('-', '.'), out r) && !rows.TryGetValue(tidm + ".", out r))
            { missed.Add(c.CompanyId); continue; }
            found[c.CompanyId] = Icb("London Stock Exchange", r.Industry, r.SuperSector, null, null, asOf ?? today);
        }
    }

    /// <summary>TIDM → ICB industry and super-sector from the instrument list's "All Equity" sheet.</summary>
    internal static (Dictionary<string, (string Industry, string SuperSector)> Rows, DateOnly? AsOf) ReadLse(byte[] xlsx)
    {
        using var book = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = book.Worksheets.FirstOrDefault(w => w.Name.Contains("All Equity", StringComparison.OrdinalIgnoreCase)) ?? book.Worksheet(1);
        var rows = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        DateOnly? asOf = null;
        Dictionary<string, int>? header = null;
        foreach (var row in sheet.RowsUsed())
        {
            var cells = row.Cells(1, row.LastCellUsed()?.Address.ColumnNumber ?? 1).Select(x => x.GetString().Trim()).ToList();
            if (asOf is null && cells.FirstOrDefault(x => x.StartsWith("As at ", StringComparison.OrdinalIgnoreCase)) is { } at &&
                DateOnly.TryParseExact(at[6..].Trim(), "d MMMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) asOf = d;
            if (header is null)
            {
                if (cells.Contains("TIDM") && cells.Contains("ICB Industry"))
                    header = cells.Select((h, i) => (h, i)).Where(x => x.h != "").GroupBy(x => x.h).ToDictionary(g => g.Key, g => g.First().i);
                continue;
            }
            string Cell(string name) => header.TryGetValue(name, out var i) && i < cells.Count ? cells[i] : "";
            var tidm = Cell("TIDM");
            var industry = Cell("ICB Industry");
            if (tidm == "" || industry == "" || Cell("MiFIR Identifier Code") is not ("SHRS" or "DPRS" or "OTHR")) continue;
            rows.TryAdd(tidm, (industry, Cell("ICB Super-Sector Name")));
        }
        return (rows, asOf);
    }

    // ---- Nasdaq Nordic and Baltic ------------------------------------------------------------------------------------

    private async Task NordicAsync(string exchange, string market, string suffix, List<Company> companies,
        Dictionary<string, IndustryClassification> found, List<string> missed, DateOnly today, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var listed = new Dictionary<string, (string Industry, string OrderbookId, string Isin)>(StringComparer.OrdinalIgnoreCase);
        var byIsin = new Dictionary<string, (string Industry, string OrderbookId, string Isin)>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in new[] { "MAIN_MARKET", "FIRST_NORTH" })
        {
            var json = await client.GetStringAsync(string.Format(CultureInfo.InvariantCulture, NasdaqNordicList, market, category), CachePolicy.Index, ct);
            foreach (var r in NordicRows(json))
            {
                listed.TryAdd(Compact(r.Symbol), (r.Sector, r.OrderbookId, r.Isin));
                // A share listed in more than one Nordic country carries the country: "NDA FI" is Nordea in Helsinki.
                if (r.Symbol.Split(' ') is [.. var head, "FI" or "SE" or "DK"] && head.Length > 0)
                    listed.TryAdd(Compact(string.Concat(head)), (r.Sector, r.OrderbookId, r.Isin));
                if (r.Isin != "") byIsin.TryAdd(r.Isin, (r.Sector, r.OrderbookId, r.Isin));
            }
        }
        var isins = OpenFigiIsins(suffix);
        foreach (var c in companies)
        {
            var ticker = Strip(c.Ticker, suffix);
            if (!listed.TryGetValue(Compact(ticker), out var row) &&
                !(isins.TryGetValue(ticker, out var isin) && byIsin.TryGetValue(isin, out row))) { missed.Add(c.CompanyId); continue; }
            var summary = await client.GetStringAsync(string.Format(CultureInfo.InvariantCulture, NasdaqNordicSummary, row.OrderbookId), CachePolicy.Index, ct);
            var (code, superSector) = NordicSummary(summary);
            found[c.CompanyId] = Icb(exchange, row.Industry, superSector, code, null, today);
        }
    }

    internal static IEnumerable<(string Symbol, string Sector, string OrderbookId, string Isin)> NordicRows(string? json)
    {
        if (json is null) yield break;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("instrumentListing", out var listing) || !listing.TryGetProperty("rows", out var rows)) yield break;
        foreach (var r in rows.EnumerateArray())
            yield return (Str(r, "symbol"), Str(r, "sector"), Str(r, "orderbookId"), Str(r, "isin"));
    }

    /// <summary>The ICB code (super-sector, four digits) and its name from a share's summary.</summary>
    internal static (string? Code, string? SuperSector) NordicSummary(string? json)
    {
        if (json is null) return (null, null);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("summaryData", out var s)) return (null, null);
        string? Value(string key) => s.TryGetProperty(key, out var p) && p.TryGetProperty("value", out var v) && v.GetString() is { Length: > 0 } x ? x.Trim() : null;
        return (Value("icbCode"), Value("sectorName"));
    }

    private async Task BalticAsync(List<Company> companies, Dictionary<string, IndustryClassification> found, List<string> missed,
        DateOnly today, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var bytes = await client.GetBytesAsync(NasdaqBalticList, CachePolicy.Index, ct);
        if (bytes is null) return;
        var rows = new Dictionary<string, (string Industry, string SuperSector)>(StringComparer.OrdinalIgnoreCase);
        using (var book = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = book.Worksheet(1);
            var header = sheet.FirstRowUsed()!.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber);
            foreach (var row in sheet.RowsUsed().Skip(1))
                rows.TryAdd(row.Cell(header["Ticker"]).GetString().Trim(),
                    (row.Cell(header["Industry"]).GetString().Trim(), row.Cell(header["Supersector"]).GetString().Trim()));
        }
        foreach (var c in companies)
        {
            var ticker = c.Ticker.Contains('.') ? c.Ticker[..c.Ticker.LastIndexOf('.')] : c.Ticker;
            if (!rows.TryGetValue(ticker, out var r)) { missed.Add(c.CompanyId); continue; }
            found[c.CompanyId] = Icb(c.Exchange, r.Industry, r.SuperSector, null, null, today);
        }
    }

    // ---- Euronext --------------------------------------------------------------------------------------------------

    private async Task EuronextAsync(List<Company> companies, Dictionary<string, IndustryClassification> found, List<string> missed,
        DateOnly today, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var isins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var suffix in companies.Select(c => Suffix(c.Ticker)).Distinct())
            foreach (var (t, i) in OpenFigiIsins(suffix)) isins.TryAdd(t + suffix, i);
        var listed = EuronextRows(await client.GetStringAsync(EuronextList, CachePolicy.Index, ct)).ToList();
        var done = 0;
        foreach (var c in companies)
        {
            ct.ThrowIfCancellationRequested();
            if (++done % 100 == 0) log.LogInformation("Euronext categories: {Done}/{Total}", done, companies.Count);
            // Euronext's own list first (symbol on the company's market), then the ISIN OpenFIGI gave the European import.
            var symbol = Strip(c.Ticker, Suffix(c.Ticker));
            var row = listed.Where(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && r.Market.Contains(EuronextCity[c.Exchange], StringComparison.Ordinal))
                .OrderBy(r => MicOf(r.Market) is null).FirstOrDefault();
            var isin = row.Isin ?? isins.GetValueOrDefault(c.Ticker);
            if (isin is null) { missed.Add(c.CompanyId + " (no ISIN)"); continue; }
            EuronextLevels? levels = null;
            var first = row.Market is { } m && MicOf(m) is { } own ? [own] : Array.Empty<string>();
            foreach (var mic in first.Concat(EuronextMics[c.Exchange]).Concat(AnyEuronext).Distinct())
            {
                var html = await client.GetStringAsync(string.Format(CultureInfo.InvariantCulture, EuronextIcb, isin, mic), CachePolicy.Index, ct);
                if ((levels = ReadEuronext(html)) is not null) break;
            }
            if (levels is null) { missed.Add(c.CompanyId); continue; }
            var (code, name) = levels.Subsector ?? levels.Sector ?? levels.SuperSector!.Value;
            found[c.CompanyId] = new IndustryClassification
            {
                Scheme = "ICB", Code = code, Industry = levels.Industry?.Name, Name = name, Source = c.Exchange, CheckedOn = today
            };
        }
    }

    /// <summary>Name;ISIN;Symbol;Market;… rows of Euronext's share list (four header lines first).</summary>
    internal static IEnumerable<(string Isin, string Symbol, string Market)> EuronextRows(string? csv)
    {
        foreach (var line in (csv ?? "").Split('\n'))
        {
            var f = line.TrimEnd('\r').Split(';').Select(x => x.Trim().Trim('"')).ToArray();
            if (f.Length >= 4 && IsinPattern().IsMatch(f[1])) yield return (f[1], f[2], f[3]);
        }
    }

    internal sealed record EuronextLevels((string Code, string Name)? Industry, (string Code, string Name)? SuperSector,
        (string Code, string Name)? Sector, (string Code, string Name)? Subsector);

    /// <summary>The ICB levels from Euronext's factsheet block ("Industry / 35, Real Estate", "SuperSector / 3510, …").</summary>
    internal static EuronextLevels? ReadEuronext(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var lines = Tag().Replace(html, "\n").Split('\n').Select(l => WebUtility.HtmlDecode(l).Trim()).Where(l => l != "").ToList();
        (string, string)? Level(string label)
        {
            var i = lines.FindIndex(l => l.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 1 >= lines.Count) return null;
            var m = CodeName().Match(lines[i + 1]);
            return m.Success ? (m.Groups[1].Value, m.Groups[2].Value.Trim()) : null;
        }
        var levels = new EuronextLevels(Level("Industry"), Level("SuperSector"), Level("Sector"), Level("Subsector"));
        return levels.SuperSector is null && levels.Sector is null && levels.Subsector is null ? null : levels;
    }

    // ---- ASX -------------------------------------------------------------------------------------------------------

    private async Task AsxAsync(List<Company> companies, Dictionary<string, IndustryClassification> found, List<string> missed,
        DateOnly today, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var csv = await client.GetStringAsync(AsxList, CachePolicy.Index, ct);
        if (csv is null) return;
        var rows = Csv.Parse(csv).Where(r => r.Count >= 3).ToDictionary(r => r[1].Trim(), r => r[2].Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in companies)
        {
            if (!rows.TryGetValue(Strip(c.Ticker, ".AX"), out var group) || Gics.Code(group) is not { } code) { missed.Add(c.CompanyId); continue; }
            found[c.CompanyId] = new IndustryClassification
            {
                Scheme = "GICS", Code = code, Industry = Gics.Sector(group), Name = group, Source = "ASX", CheckedOn = today
            };
        }
    }

    // ---- Madrid ----------------------------------------------------------------------------------------------------

    /// <summary>BME's own sector and subsector for each share on the continuous market, matched by ISIN.</summary>
    private async Task MadridAsync(List<Company> companies, Dictionary<string, IndustryClassification> found, List<string> missed,
        DateOnly today, CancellationToken ct)
    {
        if (companies.Count == 0) return;
        var names = new Dictionary<string, (string Sector, string Subsector)>();
        var sectorsJson = await client.GetStringAsync(BmeSectors, CachePolicy.Index, ct);
        var listJson = await client.GetStringAsync(BmeCompanies, CachePolicy.Index, ct);
        if (sectorsJson is null || listJson is null) return;
        using (var doc = JsonDocument.Parse(sectorsJson))
            foreach (var sector in doc.RootElement.GetProperty("data").EnumerateArray())
                foreach (var sub in sector.GetProperty("subsectors").EnumerateArray())
                    names[$"{Str(sector, "code")}.{Str(sub, "code")}"] = (Str(sector, "name"), Str(sub, "name"));
        var byIsin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(listJson))
            foreach (var r in doc.RootElement.GetProperty("data").EnumerateArray())
                byIsin.TryAdd(Str(r, "isin"), $"{Str(r, "sector")}.{Str(r, "subsector")}");
        var isins = OpenFigiIsins(".MC");
        foreach (var c in companies)
        {
            if (!isins.TryGetValue(Strip(c.Ticker, ".MC"), out var isin) || !byIsin.TryGetValue(isin, out var code) || !names.TryGetValue(code, out var n))
            { missed.Add(c.CompanyId); continue; }
            found[c.CompanyId] = new IndustryClassification
            {
                Scheme = "BME", Code = code, Industry = n.Sector, Name = n.Subsector, Source = "Bolsa de Madrid", CheckedOn = today
            };
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    /// <summary>An ICB classification from names (and a code where the exchange gives one).</summary>
    internal static IndustryClassification Icb(string source, string? industry, string? superSector, string? code, string? finer, DateOnly checkedOn)
    {
        industry = Clean(industry);
        superSector = Clean(superSector);
        code ??= global::CompanyPaisa.Importer.Icb.SuperSectorCode(superSector) ?? global::CompanyPaisa.Importer.Icb.IndustryCode(industry);
        // Use the scheme's own spelling, whatever the exchange's.
        if (code is { Length: >= 4 } && global::CompanyPaisa.Importer.Icb.SuperSectors.TryGetValue(code[..4], out var canonical)) superSector = canonical;
        if (code is { Length: >= 2 } && global::CompanyPaisa.Importer.Icb.Industries.TryGetValue(code[..2], out var top)) industry = top;
        return new IndustryClassification
        {
            Scheme = "ICB", Code = code, Industry = industry, Name = finer ?? superSector ?? industry ?? "", Source = source, CheckedOn = checkedOn
        };
    }

    /// <summary>OpenFIGI's ticker → ISIN for one market, from the lists the European import saved (data/cache/eu).</summary>
    private Dictionary<string, string> OpenFigiIsins(string suffix)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var country = suffix switch { ".PA" => "fr", ".AS" => "nl", ".BR" => "be", ".OL" => "no", ".MI" => "it", ".ST" => "se", ".CO" => "dk", ".HE" => "fi", ".MC" => "es", ".VI" => "at", _ => null };
        var path = country is null ? null : Path.Combine(euCacheDirectory, $"openfigi-{country}.json");
        if (path is null || !File.Exists(path)) return map;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        // Real ISINs only: the file also keeps name searches ("name:<LEI>"), which aren't share identifiers.
        foreach (var p in doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && IsinPattern().IsMatch(p.Name)))
            if (Str(p.Value, "Ticker") is { Length: > 0 } t && (!map.ContainsKey(t) || Str(p.Value, "SecurityType").Contains("Common", StringComparison.OrdinalIgnoreCase)))
                map[t] = p.Name;
        return map;
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
    private static string Strip(string ticker, string suffix) => ticker.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? ticker[..^suffix.Length] : ticker;
    private static string Suffix(string ticker) => ticker.Contains('.') ? ticker[ticker.LastIndexOf('.')..] : "";
    private static string Compact(string symbol) => symbol.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("<[^>]+>")] private static partial Regex Tag();
    [GeneratedRegex("^[A-Z]{2}[A-Z0-9]{9}[0-9]$")] private static partial Regex IsinPattern();
    [GeneratedRegex(@"^(\d{2,8}),\s*(.+)$")] private static partial Regex CodeName();
}
