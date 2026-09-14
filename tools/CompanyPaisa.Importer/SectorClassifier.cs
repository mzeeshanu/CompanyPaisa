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
        >= 1300 and <= 1389 or >= 2900 and <= 2999 or >= 4900 and <= 4991 => "Energy & utilities",
        >= 1000 and <= 1499 or >= 2800 and <= 2829 or >= 3300 and <= 3399 => "Materials",
        >= 4000 and <= 4799 => "Transportation",
        >= 4800 and <= 4899 or >= 2700 and <= 2799 => "Media & telecom",
        >= 8200 and <= 8299 => "Education",
        >= 2000 and <= 2399 or >= 2500 and <= 2599 or >= 3900 and <= 3999 or >= 5000 and <= 5999 or >= 7000 and <= 7299 => "Consumer & retail",
        >= 1500 and <= 1799 or >= 2400 and <= 2499 or >= 3000 and <= 3999 => "Industrials",
        >= 7380 and <= 8999 => "Business services",
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
}
