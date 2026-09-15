using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Importer.Eu;

/// <summary>
/// European import settings (appsettings section "Importer:Eu"; run with --eu). Financials only: revenue and profit from
/// ESEF annual reports on filings.xbrl.org. Paths are relative to the repo root.
/// </summary>
public sealed class EuOptions
{
    /// <summary>Generic identity — no personal contact details are sent to these sources.</summary>
    public string UserAgent { get; set; } = "CompanyPaisa";
    [Range(1, 10)] public int MaxRequestsPerSecond { get; set; } = 4;
    public string CacheDirectory { get; set; } = "data/cache/eu";
    [Range(0, 8760)] public int IndexCacheHours { get; set; } = 24;

    public string FilingsApi { get; set; } = "https://filings.xbrl.org";
    public string GleifApi { get; set; } = "https://api.gleif.org/api/v1";
    /// <summary>GeoNames postcode files, one per country: {0} = country code (CC BY 4.0).</summary>
    public string PostcodesUrl { get; set; } = "https://download.geonames.org/export/zip/{0}.zip";
    /// <summary>Only companies whose latest annual report ended within this many years (still listed).</summary>
    [Range(1, 10)] public int RecentYears { get; set; } = 2;

    [MinLength(1)] public List<EuCountryOptions> Countries { get; set; } = [];

    public string ReportPath { get; set; } = "data/import-report-eu.md";
    /// <summary>Postcode table for the API ("FR:75008,Paris,FR,FR,…").</summary>
    public string PostcodeTableOutput { get; set; } = "data/reference/eu-postcodes.csv";
    /// <summary>Reviewable list of the companies found (ticker, name, lei), per run.</summary>
    public string ListPath { get; set; } = "data/curated/eu-companies.csv";
}

public sealed class EuCountryOptions
{
    /// <summary>ISO country code, e.g. "FR". Also the prefix of the country's own ISINs.</summary>
    [Required] public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Shown as the exchange, e.g. "Euronext Paris".</summary>
    public string Exchange { get; set; } = "";
    /// <summary>OpenFIGI (Bloomberg) exchange codes, tried in order: FP Paris, NA Amsterdam, IM Milan, SM/SQ Madrid.</summary>
    [MinLength(1)] public List<string> ExchangeCodes { get; set; } = [];
    /// <summary>Appended to the ticker to keep ids unique across markets, e.g. ".PA".</summary>
    [Required] public string TickerSuffix { get; set; } = "";
    /// <summary>Named areas for the report; a region with WholeCountry takes everything else.</summary>
    public List<RegionOptions> Regions { get; set; } = [];
}
