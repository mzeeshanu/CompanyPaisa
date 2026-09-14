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
    public GeoImportOptions Geo { get; set; } = new();
    public OutputOptions Output { get; set; } = new();
    /// <summary>UK market (run with --uk).</summary>
    public Uk.UkOptions Uk { get; set; } = new();
    /// <summary>Hand-curated sites of companies headquartered elsewhere (Adobe Lehi, eBay Draper…).</summary>
    public string CuratedOfficesPath { get; set; } = "data/curated/utah-offices.csv";
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
    /// <summary>Two-letter states searched with EDGAR full-text search (business address). Cover every state a metro touches.</summary>
    [MinLength(1)] public List<string> States { get; set; } = [];
    public List<string> Forms { get; set; } = [];
    /// <summary>Only companies that filed one of <see cref="Forms"/> since this date (i.e. still reporting).</summary>
    public DateOnly FiledSince { get; set; } = new(2024, 6, 1);
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
    /// <summary>Existing CSV (zip,city,state,…) used to name ZIPs; SEC business addresses add more names.</summary>
    public string ZipNamesSeed { get; set; } = "data/reference/us-zip-centroids.sample.csv";
}

public sealed class OutputOptions
{
    public string WorkbookPath { get; set; } = "data/companypaisa.xlsx";
    public string ReportPath { get; set; } = "data/import-report.md";
}
