using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Importer.Pk;

/// <summary>
/// Pakistan import settings (appsettings section "Importer:Pk"; run with --market pk). Companies listed on the Pakistan
/// Stock Exchange: the list and profiles from the exchange's data portal, figures read from each company's own annual
/// report (the PDF it files with the exchange). The portal's financial tables are licensed data and are not used.
/// Paths are relative to the repo root.
/// </summary>
public sealed class PkOptions
{
    /// <summary>Generic identity — no personal contact details are sent to these sources.</summary>
    public string UserAgent { get; set; } = "CompanyPaisa";
    [Range(1, 10)] public int MaxRequestsPerSecond { get; set; } = 2;
    public string CacheDirectory { get; set; } = "data/cache/pk";
    [Range(0, 8760)] public int IndexCacheHours { get; set; } = 72;

    public string PsxBase { get; set; } = "https://dps.psx.com.pk";
    /// <summary>GeoNames postcodes (CC BY 4.0).</summary>
    public string PostcodesUrl { get; set; } = "https://download.geonames.org/export/zip/PK.zip";
    /// <summary>GeoNames towns of 5,000+ people (CC BY 4.0): an address's city and its position.</summary>
    public string CitiesUrl { get; set; } = "https://download.geonames.org/export/dump/cities5000.zip";

    /// <summary>Only companies whose latest annual report covers a year that ended within this many months.</summary>
    [Range(6, 60)] public int RecentMonths { get; set; } = 24;
    /// <summary>Annual reports read per company: the latest gives two years, each older one a year more.</summary>
    [Range(1, 4)] public int ReportsPerCompany { get; set; } = 2;
    /// <summary>Reports read at the same time (reading a 300-page PDF takes a few seconds).</summary>
    [Range(1, 16)] public int Parallelism { get; set; } = 8;
    /// <summary>Import only these symbols (testing); empty = every listed company.</summary>
    public List<string> OnlySymbols { get; set; } = [];

    /// <summary>Exchange sectors that aren't operating companies (funds, modarabas).</summary>
    public List<string> ExcludedSectors { get; set; } = ["MODARABAS", "CLOSE - END MUTUAL FUND"];
    public string Exchange { get; set; } = "Pakistan Stock Exchange";
    /// <summary>Appended to the symbol to keep ids unique across markets ("LUCK.KA", as Reuters and Yahoo write it).</summary>
    [Required] public string TickerSuffix { get; set; } = ".KA";

    /// <summary>Named areas for the report; a region with WholeCountry takes everything else.</summary>
    public List<RegionOptions> Regions { get; set; } = [];

    public string ReportPath { get; set; } = "data/import-report-pk.md";
    /// <summary>Postcode table for the API ("PK:74000,Karachi,PK,PK,…").</summary>
    public string PostcodeTableOutput { get; set; } = "data/reference/pk-postcodes.csv";
}
