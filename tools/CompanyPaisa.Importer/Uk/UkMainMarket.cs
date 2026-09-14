using System.Net.Http.Json;
using System.Text.Json;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Uk;

/// <summary>
/// Every UK company on the London Stock Exchange's Main Market that files ESEF annual reports, not just the FTSE 350:
/// filings.xbrl.org lists the filers (by LEI), GLEIF gives each one's ISINs, and OpenFIGI says which ISIN is ordinary
/// shares traded in London and under what ticker. Bond-only issuers and investment trusts (OpenFIGI: "Closed-End Fund")
/// fall out because they have no London-listed ordinary shares.
/// The same lookup serves the European markets: <c>exchangeCodes</c> are OpenFIGI (Bloomberg) exchange codes (["LN"] London,
/// ["FP"] Euronext Paris…), <c>isinPrefixes</c> the market's own ISINs ("GB00", "FR"…), <c>cacheName</c> its cache file.
/// </summary>
public sealed class UkMainMarket(ISecClient client, string gleifApi, string cacheDirectory, ILogger logger,
    IReadOnlyList<string>? exchangeCodes = null, IReadOnlyList<string>? isinPrefixes = null, string cacheName = "openfigi-isins.json") : IDisposable
{
    private readonly HttpClient _figi = new() { BaseAddress = new Uri("https://api.openfigi.com/"), Timeout = TimeSpan.FromSeconds(60), DefaultRequestHeaders = { { "User-Agent", "CompanyPaisa" } } };
    private readonly string _cacheFile = Path.Combine(cacheDirectory, cacheName);
    private readonly IReadOnlyList<string> _exchanges = exchangeCodes ?? ["LN"];
    private readonly IReadOnlyList<string> _prefixes = isinPrefixes ?? EquityPrefixes;
    private Dictionary<string, FigiListing?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What OpenFIGI says an ISIN trades as in London (null = nothing listed there).</summary>
    public sealed record FigiListing(string Ticker, string SecurityType, string Name);

    /// <summary>Ordinary shares and REITs count as companies; funds and trusts don't.</summary>
    private static readonly HashSet<string> CompanyShareTypes = new(StringComparer.OrdinalIgnoreCase) { "Common Stock", "REIT", "Depositary Receipt" };

    /// <summary>Home-market equity ISIN prefixes (UK and Crown Dependencies, Ireland). Bonds are mostly XS…/US….</summary>
    private static readonly string[] EquityPrefixes = ["GB00", "JE00", "GG00", "IM00", "IE00"];

    /// <summary>
    /// Constituents for filers the FTSE list doesn't already cover: ticker from OpenFIGI, sector unknown ("").
    /// Only filers with an annual report ending in the last <paramref name="recentYears"/> years (still listed).
    /// </summary>
    public async Task<List<UkConstituent>> FindAsync(IEnumerable<UkEntity> unclaimed, ISet<string> takenTickers, int recentYears, CancellationToken ct)
    {
        LoadCache();
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-recentYears);
        var candidates = unclaimed.Where(e => e.Filings.Count > 0 && e.Filings.Max(f => f.PeriodEnd) >= cutoff).ToList();
        logger.LogInformation("Looking up {Exchanges} tickers for {Count} ESEF filers", string.Join("/", _exchanges), candidates.Count);

        // 1. ISINs per filer (GLEIF), keeping home-market equity-looking ones.
        var isinsByLei = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in candidates)
        {
            var json = await client.GetStringAsync($"{gleifApi}/lei-records/{e.Lei}/isins?page%5Bsize%5D=200", CachePolicy.Index, ct);
            if (json is null) continue;
            using var doc = JsonDocument.Parse(json);
            // Shares usually have the oldest-style codes ("FR0000131104"); banks list hundreds of bonds, so try those first.
            isinsByLei[e.Lei] = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(d => d.GetProperty("attributes").GetProperty("isin").GetString() ?? "")
                .Where(i => _prefixes.Any(p => i.StartsWith(p, StringComparison.Ordinal))).Distinct()
                .OrderBy(i => i.Length > 6 && i[2..6] == "0000" ? 0 : 1).Take(20).ToList();
        }

        // 2. Home-exchange listings per ISIN (OpenFIGI, cached — ISINs don't change).
        await MapAsync(isinsByLei.Values.SelectMany(v => v).Distinct().Where(i => !_cache.ContainsKey(i)).ToList(), ct);
        SaveCache();

        FigiListing? ByIsin(UkEntity e) => isinsByLei.GetValueOrDefault(e.Lei, [])
            .Select(i => _cache.GetValueOrDefault(i))
            .FirstOrDefault(l => l is not null && CompanyShareTypes.Contains(l.SecurityType));

        // 3. Still nothing (BNP Paribas: its share ISIN is buried under bond ISINs): search the exchange by the company's name.
        foreach (var e in candidates.Where(e => ByIsin(e) is null && !_cache.ContainsKey(SearchKey(e))))
        {
            _cache[SearchKey(e)] = await SearchByNameAsync(e.Name, ct);
            SaveCache();
        }

        var result = new List<UkConstituent>();
        foreach (var e in candidates)
        {
            var listing = ByIsin(e) ?? _cache.GetValueOrDefault(SearchKey(e));
            if (listing is null) continue;
            var ticker = listing.Ticker.Replace('/', '.').TrimEnd('.').ToUpperInvariant();
            if (!takenTickers.Add(ticker)) continue;
            result.Add(new UkConstituent(ticker, TidyName(e.Name), "", e.Lei));
        }
        logger.LogInformation("{Count} companies with shares listed on {Exchanges}", result.Count, string.Join("/", _exchanges));
        return result;
    }

    private static string SearchKey(UkEntity e) => "name:" + e.Lei;

    /// <summary>
    /// OpenFIGI search (no API key: about 5 a minute) for ordinary shares on the home exchange under the company's name.
    /// Only an exact match of the cleaned names counts, so "BNP Paribas Home Loan SFH" never borrows BNP Paribas's shares.
    /// </summary>
    private async Task<FigiListing?> SearchByNameAsync(string legalName, CancellationToken ct)
    {
        var wanted = UkImportPipeline.NormaliseName(legalName);
        if (wanted.Length < 3) return null;
        foreach (var exchange in _exchanges)
        {
            for (var attempt = 1; ; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(12.5), ct);
                using var response = await _figi.PostAsJsonAsync("v3/search", new { query = legalName, exchCode = exchange, securityType2 = "Common Stock" }, ct);
                if ((int)response.StatusCode == 429 && attempt < 5) { await Task.Delay(TimeSpan.FromSeconds(30 * attempt), ct); continue; }
                if (!response.IsSuccessStatusCode) { logger.LogWarning("OpenFIGI search returned {Status} for {Name}", response.StatusCode, legalName); return null; }
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("data", out var data))
                    foreach (var d in data.EnumerateArray())
                    {
                        var name = d.GetProperty("name").GetString() ?? "";
                        if (UkImportPipeline.NormaliseName(name) == wanted)
                            return new FigiListing(d.GetProperty("ticker").GetString() ?? "", d.TryGetProperty("securityType2", out var t) ? t.GetString() ?? "" : "", name);
                    }
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// OpenFIGI without an API key: 10 ISINs per request, 25 requests a minute. Each exchange code is tried in turn
    /// (Spain trades on "SM" and "SQ"); an ISIN listed on none of them is remembered as null.
    /// </summary>
    private async Task MapAsync(List<string> isins, CancellationToken ct)
    {
        var pending = isins;
        foreach (var exchange in _exchanges)
        {
            var found = new Dictionary<string, FigiListing>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in pending.Chunk(10))
            {
                var jobs = batch.Select(i => new { idType = "ID_ISIN", idValue = i, exchCode = exchange }).ToList();
                for (var attempt = 1; ; attempt++)
                {
                    using var response = await _figi.PostAsJsonAsync("v3/mapping", jobs, ct);
                    if ((int)response.StatusCode == 429 && attempt < 5) { await Task.Delay(TimeSpan.FromSeconds(30 * attempt), ct); continue; }
                    if (!response.IsSuccessStatusCode) { logger.LogWarning("OpenFIGI returned {Status}; {Count} ISINs left unmapped this run", response.StatusCode, batch.Length); break; }
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    var results = doc.RootElement.EnumerateArray().ToList();
                    for (var i = 0; i < batch.Length && i < results.Count; i++)
                        if (results[i].TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                            found[batch[i]] = data.EnumerateArray().Select(d => new FigiListing(d.GetProperty("ticker").GetString() ?? "", d.TryGetProperty("securityType2", out var t) ? t.GetString() ?? "" : "", d.GetProperty("name").GetString() ?? ""))
                                .OrderBy(l => CompanyShareTypes.Contains(l.SecurityType) ? 0 : 1).First();
                    foreach (var isin in batch) _cache[isin] = found.GetValueOrDefault(isin);
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(2.6), ct);
            }
            pending = pending.Where(i => !found.ContainsKey(i)).ToList();
            if (pending.Count == 0) break;
        }
    }

    /// <summary>"TESCO PLC" → "Tesco plc"-style names read better; names that already have lower case are kept.</summary>
    internal static string TidyName(string legal)
    {
        if (legal.Any(char.IsLower)) return legal.Trim();
        var t = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(legal.Trim().ToLowerInvariant());
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\b(Reit|Ip|Uk|Fs|Ai|It|Bp|Hsbc|Ii|Iii|Se|Sa|Nv|Bv|Ag|Sca|Bnp|Ing|Asml|Axa|Eni|Stmicro|Bbva|Acs|Nn|Asr|Abn|Amro|Kpn|Rai|Mfe|Scor)\b", m => m.Value.ToUpperInvariant());
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\bS\.?P\.?A\.?$", "S.p.A.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(t, @"\bPlc\b", "plc");
    }

    private void LoadCache()
    {
        if (File.Exists(_cacheFile))
            _cache = JsonSerializer.Deserialize<Dictionary<string, FigiListing?>>(File.ReadAllText(_cacheFile)) is { } c
                ? new Dictionary<string, FigiListing?>(c, StringComparer.OrdinalIgnoreCase) : _cache;
    }

    private void SaveCache()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
        File.WriteAllText(_cacheFile, JsonSerializer.Serialize(_cache));
    }

    public void Dispose() => _figi.Dispose();
}
