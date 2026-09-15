using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Importer.Uk;

/// <summary>UK import settings (appsettings section "Importer:Uk"). Paths are relative to the repo root.</summary>
public sealed class UkOptions
{
    /// <summary>Generic identity for the UK sources — no personal contact details are sent to them.</summary>
    public string UserAgent { get; set; } = "CompanyPaisa";
    [Range(1, 10)] public int MaxRequestsPerSecond { get; set; } = 4;
    public string CacheDirectory { get; set; } = "data/cache/uk";
    [Range(0, 8760)] public int IndexCacheHours { get; set; } = 24;

    /// <summary>Reviewable list of index members (ticker, name, sector, lei). Rebuilt with --refresh-uk-list.</summary>
    public string ConstituentsPath { get; set; } = "data/curated/uk-ftse350.csv";
    /// <summary>Wikipedia pages whose constituent tables make up the list.</summary>
    public List<string> ConstituentPages { get; set; } = ["FTSE_100_Index", "FTSE_250_Index"];
    /// <summary>ICB sectors left out: investment trusts and funds have no operating revenue or executives.</summary>
    /// <summary>
    /// Also every other UK Main Market company that files ESEF reports (tickers via GLEIF ISINs + OpenFIGI), not only the FTSE 350.
    /// </summary>
    public bool AllMainMarket { get; set; }
    /// <summary>Review copy of the companies added beyond the FTSE 350 (rewritten every run).</summary>
    public string MainMarketListPath { get; set; } = "data/curated/uk-main-market.csv";
    public List<string> ExcludedSectors { get; set; } = ["Closed End Investments", "Investment Trusts", "Collective Investments", "Open End and Miscellaneous Investment Vehicles"];

    /// <summary>ESEF annual reports (xBRL-JSON + the report itself), indexed by LEI.</summary>
    public string FilingsApi { get; set; } = "https://filings.xbrl.org";
    /// <summary>Global LEI registry — headquarters addresses.</summary>
    public string GleifApi { get; set; } = "https://api.gleif.org/api/v1";
    /// <summary>GeoNames UK postcode districts (CC BY 4.0).</summary>
    public string PostcodeDistrictsUrl { get; set; } = "https://download.geonames.org/export/zip/GB.zip";
    public string PostcodeTableOutput { get; set; } = "data/reference/uk-postcode-districts.csv";

    /// <summary>How many of the latest annual reports to read for directors' pay (each shows two years).</summary>
    [Range(1, 10)] public int PayReports { get; set; } = 3;

    /// <summary>Named areas for the report and the website; a region with States ["UK"] takes everything else.</summary>
    public List<RegionOptions> Regions { get; set; } = [];

    public string ReportPath { get; set; } = "data/import-report-uk.md";
}
