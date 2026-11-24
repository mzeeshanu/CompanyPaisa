using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer;

/// <summary>Maps an SEC Standard Industrial Classification code to the app's broad sectors.</summary>
public static class SectorClassifier
{
    public static string FromSic(int? sic) => sic switch
    {
        null => "Other",
        3674 => "Semiconductors",
        >= 7370 and <= 7379 => "Software & IT",
        >= 3570 and <= 3579 or >= 3660 and <= 3699 => "Technology hardware",
        >= 2830 and <= 2836 or >= 3840 and <= 3851 or >= 8000 and <= 8099 or 5047 or 5122 => "Healthcare",
        >= 6500 and <= 6553 or 6798 => "Real estate",
        >= 6000 and <= 6799 => "Finance",
        >= 2840 and <= 2844 => "Consumer & retail",   // soaps, cleaners, perfumes and cosmetics
        >= 100 and <= 999 => "Consumer & retail",     // agriculture: crops, livestock, fishing (food producers)
        >= 1300 and <= 1389 or >= 2900 and <= 2999 or >= 4900 and <= 4991 => "Energy & utilities",
        >= 1000 and <= 1499 or >= 2600 and <= 2699 or >= 2800 and <= 2899 or >= 3300 and <= 3399 => "Materials",   // mining, paper, chemicals, metals
        >= 4000 and <= 4799 => "Transportation",
        >= 4800 and <= 4899 or >= 2700 and <= 2799 => "Media & telecom",
        >= 8200 and <= 8299 => "Education",
        >= 2000 and <= 2399 or >= 2500 and <= 2599 or >= 3900 and <= 3999 or >= 5000 and <= 5999 or >= 7000 and <= 7299 => "Consumer & retail",
        >= 1500 and <= 1799 or >= 2400 and <= 2499 or >= 3000 and <= 3999 => "Industrials",
        >= 7300 and <= 7369 or >= 7380 and <= 8999 => "Business services",   // advertising, credit agencies, building services, rental, staffing…
        _ => "Other"
    };

    /// <summary>Maps an FTSE Russell ICB sector name (UK constituent lists) to the same broad sectors.</summary>
    public static string FromIcb(string? icb)
    {
        var s = (icb ?? "").ToLowerInvariant();
        (string Keyword, string Sector)[] map =
        [
            ("real estate", "Real estate"), ("software", "Software & IT"), ("computer", "Software & IT"),
            ("technology hardware", "Technology hardware"), ("semiconductor", "Semiconductors"),
            ("pharma", "Healthcare"), ("biotech", "Healthcare"), ("health", "Healthcare"), ("medical", "Healthcare"),
            ("bank", "Finance"), ("insurance", "Finance"), ("financ", "Finance"), ("investment", "Finance"), ("asset manag", "Finance"),
            ("oil", "Energy & utilities"), ("gas", "Energy & utilities"), ("electricity", "Energy & utilities"), ("energy", "Energy & utilities"), ("utilit", "Energy & utilities"),
            ("mining", "Materials"), ("metals", "Materials"), ("chemical", "Materials"), ("materials", "Materials"),
            ("telecom", "Media & telecom"), ("media", "Media & telecom"),
            ("transport", "Transportation"), ("airline", "Transportation"),
            ("retail", "Consumer & retail"), ("food", "Consumer & retail"), ("beverage", "Consumer & retail"), ("tobacco", "Consumer & retail"),
            ("personal", "Consumer & retail"), ("household", "Consumer & retail"), ("leisure", "Consumer & retail"), ("travel", "Consumer & retail"),
            ("consumer", "Consumer & retail"), ("automobile", "Consumer & retail"), ("home construction", "Consumer & retail"),
            ("support services", "Business services"), ("aerospace", "Industrials"), ("defen", "Industrials"), ("industrial", "Industrials"),
            ("construction", "Industrials"), ("engineering", "Industrials"), ("electronic", "Industrials")
        ];
        return map.FirstOrDefault(m => s.Contains(m.Keyword)).Sector ?? "Other";
    }

    /// <summary>The SIC division a code belongs to (the scheme's top level).</summary>
    public static string? SicDivision(int sic) => sic switch
    {
        >= 100 and <= 999 => "Agriculture, Forestry and Fishing",
        >= 1000 and <= 1499 => "Mining",
        >= 1500 and <= 1799 => "Construction",
        >= 2000 and <= 3999 => "Manufacturing",
        >= 4000 and <= 4999 => "Transportation, Communications and Utilities",
        >= 5000 and <= 5199 => "Wholesale Trade",
        >= 5200 and <= 5999 => "Retail Trade",
        >= 6000 and <= 6799 => "Finance, Insurance and Real Estate",
        >= 7000 and <= 8999 => "Services",
        >= 9100 and <= 9729 => "Public Administration",
        _ => null
    };

    /// <summary>The broad sector for Bolsa de Madrid's sector.subsector code ("02.03" construction).</summary>
    public static string? FromBme(string? code) => code switch
    {
        null => null,
        "02.01" or "02.05" or "03.03" => "Materials",
        "03.05" => "Healthcare",
        "04.03" or "06.01" => "Media & telecom",
        "04.04" or "04.05" => "Transportation",
        "04.06" => "Business services",
        "06.02" => "Software & IT",
        "06.03" => "Technology hardware",
        _ => code[..Math.Min(2, code.Length)] switch
        {
            "01" => "Energy & utilities",
            "02" => "Industrials",
            "03" or "04" => "Consumer & retail",
            "05" => "Finance",
            "07" => "Real estate",
            _ => null
        }
    };

    /// <summary>The broad sector for a company's official classification (ICB, GICS, SIC or the PSX sector).</summary>
    public static string FromClassification(IndustryClassification k, string fallback) => k.Scheme switch
    {
        "ICB" => Icb.Broad(k.Code) ?? (FromIcb(k.Name) is var byName && byName != "Other" ? byName : FromIcb(k.Industry)),
        "GICS" => Gics.Broad(k.Name) ?? fallback,
        "SIC" => int.TryParse(k.Code, out var sic) ? FromSic(sic) : fallback,
        "BME" => FromBme(k.Code) ?? fallback,
        _ => fallback
    };
}

/// <summary>
/// FTSE Russell's Industry Classification Benchmark (2019 structure), which London, Euronext and Nasdaq Nordic/Baltic use
/// for every listing: 11 industries, 20 super-sectors (codes 10–65 and 1010–6510).
/// </summary>
public static class Icb
{
    public static readonly IReadOnlyDictionary<string, string> Industries = new Dictionary<string, string>
    {
        ["10"] = "Technology", ["15"] = "Telecommunications", ["20"] = "Health Care", ["30"] = "Financials", ["35"] = "Real Estate",
        ["40"] = "Consumer Discretionary", ["45"] = "Consumer Staples", ["50"] = "Industrials", ["55"] = "Basic Materials",
        ["60"] = "Energy", ["65"] = "Utilities"
    };

    public static readonly IReadOnlyDictionary<string, string> SuperSectors = new Dictionary<string, string>
    {
        ["1010"] = "Technology", ["1510"] = "Telecommunications", ["2010"] = "Health Care", ["3010"] = "Banks",
        ["3020"] = "Financial Services", ["3030"] = "Insurance", ["3510"] = "Real Estate", ["4010"] = "Automobiles and Parts",
        ["4020"] = "Consumer Products and Services", ["4030"] = "Media", ["4040"] = "Retail", ["4050"] = "Travel and Leisure",
        ["4510"] = "Food, Beverage and Tobacco", ["4520"] = "Personal Care, Drug and Grocery Stores",
        ["5010"] = "Construction and Materials", ["5020"] = "Industrial Goods and Services", ["5510"] = "Basic Resources",
        ["5520"] = "Chemicals", ["6010"] = "Energy", ["6510"] = "Utilities"
    };

    /// <summary>Lower case, "&" as "and", single spaces: exchanges spell the same names slightly differently.</summary>
    public static string Normalise(string? name) =>
        string.Join(' ', (name ?? "").Replace("&", " and ").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static string? SuperSectorCode(string? name) =>
        SuperSectors.FirstOrDefault(p => Normalise(p.Value) == Normalise(name)).Key;

    public static string? IndustryCode(string? name) =>
        Industries.FirstOrDefault(p => Normalise(p.Value) == Normalise(name)).Key;

    /// <summary>
    /// The app's broad sector for an ICB code at any level (2, 4, 6 or 8 digits). Finer codes split the super-sectors the
    /// app separates: hardware and semiconductors from software, airlines and freight, support services, education.
    /// </summary>
    public static string? Broad(string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length < 2) return null;
        if (code.StartsWith("10102010", StringComparison.Ordinal)) return "Semiconductors";
        if (code.StartsWith("101020", StringComparison.Ordinal)) return "Technology hardware";
        if (code.StartsWith("40501010", StringComparison.Ordinal)) return "Transportation";
        if (code.StartsWith("40205020", StringComparison.Ordinal)) return "Education";
        if (code.StartsWith("502060", StringComparison.Ordinal)) return "Transportation";
        if (code.StartsWith("502050", StringComparison.Ordinal)) return "Business services";
        return code[..2] switch
        {
            "10" => "Software & IT",
            "15" => "Media & telecom",
            "20" => "Healthcare",
            "30" => "Finance",
            "35" => "Real estate",
            "40" => code.Length >= 4 && code[..4] == "4030" ? "Media & telecom" : "Consumer & retail",
            "45" => "Consumer & retail",
            "50" => "Industrials",
            "55" => "Materials",
            "60" or "65" => "Energy & utilities",
            _ => null
        };
    }
}

/// <summary>S&P/MSCI's Global Industry Classification Standard at the industry-group level (what the ASX publishes).</summary>
public static class Gics
{
    private static readonly (string Name, string Code, string Sector, string Broad)[] Groups =
    [
        ("Energy", "1010", "Energy", "Energy & utilities"),
        ("Materials", "1510", "Materials", "Materials"),
        ("Capital Goods", "2010", "Industrials", "Industrials"),
        ("Commercial & Professional Services", "2020", "Industrials", "Business services"),
        ("Transportation", "2030", "Industrials", "Transportation"),
        ("Automobiles & Components", "2510", "Consumer Discretionary", "Consumer & retail"),
        ("Consumer Durables & Apparel", "2520", "Consumer Discretionary", "Consumer & retail"),
        ("Consumer Services", "2530", "Consumer Discretionary", "Consumer & retail"),
        ("Consumer Discretionary Distribution & Retail", "2550", "Consumer Discretionary", "Consumer & retail"),
        ("Retailing", "2550", "Consumer Discretionary", "Consumer & retail"),
        ("Consumer Staples Distribution & Retail", "3010", "Consumer Staples", "Consumer & retail"),
        ("Food & Staples Retailing", "3010", "Consumer Staples", "Consumer & retail"),
        ("Food, Beverage & Tobacco", "3020", "Consumer Staples", "Consumer & retail"),
        ("Household & Personal Products", "3030", "Consumer Staples", "Consumer & retail"),
        ("Health Care Equipment & Services", "3510", "Health Care", "Healthcare"),
        ("Pharmaceuticals, Biotechnology & Life Sciences", "3520", "Health Care", "Healthcare"),
        ("Banks", "4010", "Financials", "Finance"),
        ("Financial Services", "4020", "Financials", "Finance"),
        ("Diversified Financials", "4020", "Financials", "Finance"),
        ("Insurance", "4030", "Financials", "Finance"),
        ("Software & Services", "4510", "Information Technology", "Software & IT"),
        ("Technology Hardware & Equipment", "4520", "Information Technology", "Technology hardware"),
        ("Semiconductors & Semiconductor Equipment", "4530", "Information Technology", "Semiconductors"),
        ("Telecommunication Services", "5010", "Communication Services", "Media & telecom"),
        ("Media & Entertainment", "5020", "Communication Services", "Media & telecom"),
        ("Utilities", "5510", "Utilities", "Energy & utilities"),
        ("Equity Real Estate Investment Trusts (REITs)", "6010", "Real Estate", "Real estate"),
        ("Real Estate Management & Development", "6020", "Real Estate", "Real estate")
    ];

    private static (string Name, string Code, string Sector, string Broad)? Find(string? name) =>
        Groups.FirstOrDefault(g => Icb.Normalise(g.Name) == Icb.Normalise(name)) is { Name: not null } g ? g : null;

    public static string? Code(string? group) => Find(group)?.Code;
    public static string? Sector(string? group) => Find(group)?.Sector;
    public static string? Broad(string? group) => Find(group)?.Broad;
}
