using System.Text.RegularExpressions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;

namespace CompanyPaisa.Core.Services;

/// <summary>The TradingView symbol for a company's share-price widgets ("NASDAQ:AAPL", "EURONEXT:MC").</summary>
public static partial class PriceSymbols
{
    /// <summary>
    /// Null when the widgets are off, the company's exchange doesn't allow its prices in them, or the ticker is one
    /// TradingView spells differently (preferred shares, units, warrants: "SNUS-PH") — better no card than "Invalid Symbol".
    /// </summary>
    public static string? For(Company company, PriceWidgetOptions options)
    {
        if (!options.Enabled || !options.Exchanges.TryGetValue(company.Exchange, out var prefix) || string.IsNullOrWhiteSpace(prefix))
            return null;
        // European tickers carry the market as a suffix ("MC.PA"); share classes use a dash in SEC data ("BRK-B"), a dot on TradingView.
        var ticker = company.Ticker;
        var dot = ticker.LastIndexOf('.');
        if (dot > 0) ticker = ticker[..dot];
        return PlainTicker().IsMatch(ticker) ? $"{prefix}:{ticker.Replace('-', '.')}" : null;
    }

    /// <summary>Letters and digits, optionally a one-letter share class ("BRK-B").</summary>
    [GeneratedRegex("^[A-Z0-9]+(-[A-Z])?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainTicker();
}
