using System.Text.RegularExpressions;

namespace CompanyPaisa.Analytics;

/// <summary>Device type, browser and operating system from a User-Agent header — just the broad families, no versions.</summary>
public sealed partial record UserAgentInfo(string Device, string Browser, string Os, bool IsBot)
{
    public static UserAgentInfo Parse(string? userAgent)
    {
        // Every real browser sends one; an empty header is a script.
        if (string.IsNullOrWhiteSpace(userAgent)) return new("Unknown", "Unknown", "Unknown", true);
        var ua = userAgent;

        var os = ua switch
        {
            _ when Has(ua, "iPhone") || Has(ua, "iPad") || Has(ua, "iPod") => "iOS",
            _ when Has(ua, "Android") => "Android",
            _ when Has(ua, "CrOS") => "ChromeOS",
            _ when Has(ua, "Windows") => "Windows",
            _ when Has(ua, "Macintosh") || Has(ua, "Mac OS X") => "macOS",
            _ when Has(ua, "Linux") => "Linux",
            _ => "Other"
        };

        var device = ua switch
        {
            _ when Has(ua, "iPad") || Has(ua, "Tablet") || (Has(ua, "Android") && !Has(ua, "Mobile")) => "Tablet",
            _ when Has(ua, "Mobi") || Has(ua, "iPhone") => "Phone",
            _ => "Desktop"
        };

        // Order matters: Edge and Opera also say "Chrome", and Chrome also says "Safari".
        var browser = ua switch
        {
            _ when Has(ua, "Edg/") || Has(ua, "EdgA/") || Has(ua, "EdgiOS/") => "Edge",
            _ when Has(ua, "OPR/") || Has(ua, "Opera") => "Opera",
            _ when Has(ua, "SamsungBrowser/") => "Samsung Internet",
            _ when Has(ua, "Firefox/") || Has(ua, "FxiOS/") => "Firefox",
            _ when Has(ua, "CriOS/") || Has(ua, "Chrome/") => "Chrome",
            _ when Has(ua, "Safari/") => "Safari",
            _ => "Other"
        };

        return new(device, browser, os, Bot().IsMatch(ua));
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>Crawlers, link previews, uptime monitors, headless browsers and HTTP libraries.</summary>
    [GeneratedRegex(@"bot\b|bot/|crawl|spider|slurp|facebookexternalhit|embedly|preview|headless|lighthouse|pingdom|uptime|monitor|" +
                    @"curl/|wget/|python|httpclient|okhttp|go-http|java/|axios/|node-fetch|undici|postman|insomnia|scrapy|phantomjs|selenium|playwright|puppeteer",
        RegexOptions.IgnoreCase)]
    private static partial Regex Bot();
}
