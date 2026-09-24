using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Sec;

/// <summary>
/// The exact exchange of every US listing that isn't on Nasdaq, from Nasdaq Trader's free symbol directory
/// (<c>otherlisted.txt</c>). The SEC's ticker file calls NYSE, NYSE American and NYSE Arca listings all "NYSE", and Cboe
/// ones "CBOE"; the share-price widgets need the real one ("AMEX:IMO", not "NYSE:IMO").
/// </summary>
public static class ListingExchanges
{
    public const string DefaultUrl = "https://www.nasdaqtrader.com/dynamic/SymDir/otherlisted.txt";

    /// <summary>The SEC exchange names this list can make more exact.</summary>
    private static readonly HashSet<string> Refinable = new(["NYSE", "CBOE"], StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Names = new()
    {
        ["N"] = "NYSE", ["A"] = "NYSE American", ["P"] = "NYSE Arca", ["Z"] = "Cboe", ["V"] = "IEX",
    };

    /// <summary>Ticker (SEC style, "BRK-B") → exchange name. Test issues and unknown exchange codes are left out.</summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').Skip(1))
        {
            // ACT Symbol|Security Name|Exchange|CQS Symbol|ETF|Round Lot Size|Test Issue|NASDAQ Symbol; last line "File Creation Time: …".
            var f = line.TrimEnd('\r').Split('|');
            if (f.Length < 7 || f[6] == "Y" || !Names.TryGetValue(f[2], out var exchange)) continue;
            map[f[0].Replace('.', '-')] = exchange;
        }
        return map;
    }

    /// <summary>The exact exchange when the SEC's name is one of the shared ones and the list knows the ticker; else unchanged.</summary>
    public static string Refine(string ticker, string exchange, IReadOnlyDictionary<string, string> listings) =>
        Refinable.Contains(exchange) && listings.TryGetValue(ticker, out var exact) ? exact : exchange;

    /// <summary>Downloads and reads the list; empty (exchanges stay as the SEC names them) when it can't be fetched.</summary>
    public static async Task<Dictionary<string, string>> LoadAsync(string url, ILogger log, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Enrichment.CareersFinder.UserAgent);
            var map = Parse(await http.GetStringAsync(url, ct));
            log.LogInformation("Listing exchanges: {Count} NYSE / NYSE American / NYSE Arca / Cboe listings from {Url}", map.Count, url);
            return map;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Listing exchanges: couldn't read {Url} ({Message}); exchanges stay as the SEC names them", url, ex.Message);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
