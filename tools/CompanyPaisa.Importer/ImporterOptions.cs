using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Importer;

/// <summary>All importer settings (appsettings.json section "Importer"). Paths are relative to the repo root.</summary>
public sealed class ImporterOptions
{
    public const string SectionName = "Importer";

    public SecOptions Sec { get; set; } = new();
    public DiscoveryOptions Discovery { get; set; } = new();
    /// <summary>Metro areas to cover. A company is included if its address falls inside any metro's anchor circles.</summary>
    [MinLength(1)] public List<RegionOptions> Regions { get; set; } = [];
    public ListingOptions Listing { get; set; } = new();
    public HistoryOptions History { get; set; } = new();
    public NewHireOptions NewHires { get; set; } = new();
    public GeoImportOptions Geo { get; set; } = new();
    public OutputOptions Output { get; set; } = new();
    /// <summary>UK market (run with --uk).</summary>
    public Uk.UkOptions Uk { get; set; } = new();
    /// <summary>European markets, financials only (run with --eu).</summary>
    public Eu.EuOptions Eu { get; set; } = new();
    /// <summary>Hand-curated sites of companies headquartered elsewhere (Adobe Lehi, eBay Draper…).</summary>
    public string CuratedOfficesPath { get; set; } = "data/curated/utah-offices.csv";
}

/// <summary>Officer appointments and their announced packages, from 8-K Item 5.02 filings.</summary>
public sealed class NewHireOptions
{
    /// <summary>How far back to read appointment announcements.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 60)] public int Months { get; set; } = 18;
}

public sealed class SecOptions
{
    /// <summary>The SEC requires "Company contact@email" in every request's User-Agent. Set it in appsettings.Local.json.</summary>
    [Required, RegularExpression(@"^(?!.*example\.com).*@.*$", ErrorMessage = "Set Importer:Sec:UserAgent to \"CompanyPaisa you@yourdomain\" in tools/CompanyPaisa.Importer/appsettings.Local.json (the SEC requires a real contact).")]
    public string UserAgent { get; set; } = "";
    /// <summary>SEC fair-access limit is 10/s; stay under it.</summary>
    [Range(1, 10)] public int MaxRequestsPerSecond { get; set; } = 8;
    public string CacheDirectory { get; set; } = "data/cache/sec";
    /// <summary>Gzip cached responses (proxy statements shrink ~5×). Uncompressed files from older runs still read fine.</summary>
    public bool CompressCache { get; set; } = true;
    /// <summary>Re-download cached responses older than this (filings themselves never change; indexes do).</summary>
    [Range(0, 8760)] public int IndexCacheHours { get; set; } = 24;
}

public sealed class DiscoveryOptions
{
    /// <summary>
    /// Screen every company in the SEC's listed-ticker file (nationwide) instead of searching <see cref="States"/>.
    /// Every listed company has a ticker there, so nothing is lost; the address then decides the region.
    /// </summary>
    public bool AllListed { get; set; }
    /// <summary>Two-letter states searched with EDGAR full-text search (business address). Cover every state a metro touches.</summary>
    public List<string> States { get; set; } = [];
    public List<string> Forms { get; set; } = [];
    /// <summary>Only companies that filed one of <see cref="Forms"/> since this date (i.e. still reporting).</summary>
    public DateOnly? FiledSince { get; set; }
    /// <summary>When <see cref="FiledSince"/> isn't set: filed within this many months (keeps scheduled runs current).</summary>
    [Range(1, 120)] public int FiledWithinMonths { get; set; } = 15;
    public DateOnly Since => FiledSince ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-FiledWithinMonths);
}

public sealed class RegionOptions
{
    /// <summary>Human name for reports and the website, e.g. "Wasatch Front".</summary>
    public string Name { get; set; } = "";
    /// <summary>A ZIP to suggest on the website, e.g. "84043".</summary>
    public string ExampleZip { get; set; } = "";
    /// <summary>A company is in the region if its location is within the radius of any anchor.</summary>
    public List<RegionAnchor> Anchors { get; set; } = [];
    /// <summary>
    /// Whole states (e.g. ["MN"]): every listed company headquartered anywhere in them. Metro circles are checked
    /// first, so a statewide region only picks up what no metro claimed. The states must also be in Discovery:States.
    /// </summary>
    public List<string> States { get; set; } = [];
    /// <summary>"US" or "CA". Anchors and States only match addresses in this country.</summary>
    public string Country { get; set; } = "US";
    /// <summary>Everything in <see cref="Country"/> that no metro or statewide region claimed ("Rest of US").</summary>
    public bool WholeCountry { get; set; }
}

public sealed class RegionAnchor
{
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMiles { get; set; }
}

public sealed class ListingOptions
{
    /// <summary>Exchanges that count as "publicly traded" without further checks.</summary>
    public List<string> Exchanges { get; set; } = [];
    /// <summary>OTC-quoted companies are included only above this annual revenue (filters out shells).</summary>
    public decimal MinOtcRevenue { get; set; } = 5_000_000;
    /// <summary>Include SEC filers with no ticker at all.</summary>
    public bool IncludeUnlisted { get; set; }
    /// <summary>Reviewed exclusions, ticker → reason (e.g. a US listing of a company already shown from its home market).</summary>
    public Dictionary<string, string> SkipTickers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class HistoryOptions
{
    [Range(1, 20)] public int Years { get; set; } = 10;
    /// <summary>Maximum proxy statements to read per company (each covers up to 3 years).</summary>
    [Range(1, 20)] public int MaxProxiesPerCompany { get; set; } = 5;
}

public sealed class GeoImportOptions
{
    /// <summary>Census Gazetteer ZCTA file (ZIP → lat/long).</summary>
    public string GazetteerUrl { get; set; } = "";
    /// <summary>GeoNames US postal codes (CC-BY 4.0): ZIP → city and state names, and coordinates for ZIPs the Census doesn't map.</summary>
    public string PlaceNamesUrl { get; set; } = "";
    /// <summary>ZIP prefix(es) to keep in the generated ZIP table. Empty = every US ZIP.</summary>
    public List<string> ZipPrefixes { get; set; } = [];
    public string ZipTableOutput { get; set; } = "data/reference/us-zip-centroids.csv";
    /// <summary>GeoNames Canadian postal areas (FSA, e.g. "M5J"; CC-BY 4.0). Empty = no Canadian companies.</summary>
    public string CanadaPostalCodesUrl { get; set; } = "";
    public string CanadaTableOutput { get; set; } = "data/reference/ca-postal-areas.csv";
    /// <summary>Other countries whose SEC filers are included (ISO codes, e.g. ["AU", "NZ"]); postcodes from GeoNames.</summary>
    public List<string> OtherCountries { get; set; } = [];
    /// <summary>GeoNames postcode file per country: {0} = country code.</summary>
    public string OtherPostcodesUrl { get; set; } = "https://download.geonames.org/export/zip/{0}.zip";
    public string OtherTableOutput { get; set; } = "data/reference/anz-postcodes.csv";
    /// <summary>Existing CSV (zip,city,state,…) used to name ZIPs; SEC business addresses add more names.</summary>
    public string ZipNamesSeed { get; set; } = "data/reference/us-zip-centroids.sample.csv";
}

public sealed class OutputOptions
{
    /// <summary>The website's database; every importer publishes its market into it.</summary>
    public string DatabasePath { get; set; } = "data/companypaisa.db";
    /// <summary>Workbooks from before the move to SQLite, read once by --migrate-xlsx (market → path).</summary>
    public Dictionary<string, string> LegacyWorkbooks { get; set; } = new()
    {
        ["sec"] = "data/companypaisa.xlsx", ["uk"] = "data/companypaisa-uk.xlsx", ["eu"] = "data/companypaisa-eu.xlsx"
    };
    public string ReportPath { get; set; } = "data/import-report.md";
}
