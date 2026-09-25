using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Importer.Anz;

/// <summary>
/// Australia and New Zealand import settings (appsettings section "Importer:Anz"; run with --market anz). Companies
/// listed on the ASX and NZX, from Wikipedia's index tables and Wikidata; figures read from each company's annual report
/// on its own website. The exchanges' own websites are never used: their terms don't allow automated access.
/// Paths are relative to the repo root.
/// </summary>
public sealed class AnzOptions
{
    /// <summary>Wikidata, Wikipedia and GLEIF are sent this generic identity; company websites the finder's.</summary>
    public string UserAgent { get; set; } = Enrichment.CareersFinder.UserAgent;
    [Range(1, 10)] public int MaxRequestsPerSecond { get; set; } = 2;
    public string CacheDirectory { get; set; } = "data/cache/anz";
    /// <summary>Wikidata, Wikipedia and GLEIF answers are re-read after this long.</summary>
    [Range(0, 8760)] public int IndexCacheHours { get; set; } = 168;
    /// <summary>A company's website is searched for new reports again after this many days.</summary>
    [Range(0, 365)] public int SiteCacheDays { get; set; } = 30;

    public string WikidataSparql { get; set; } = "https://query.wikidata.org/sparql";
    /// <summary>Wikipedia pages whose tables list listed companies (ASX 200 constituents, the NZX main board).</summary>
    public List<string> WikipediaPages { get; set; } = ["S%26P/ASX_200", "List_of_companies_listed_on_the_New_Zealand_Exchange"];
    public string GleifApi { get; set; } = "https://api.gleif.org/api/v1";
    /// <summary>GeoNames postcode files, one per country: {0} = country code (CC BY 4.0).</summary>
    public string PostcodesUrl { get; set; } = "https://download.geonames.org/export/zip/{0}.zip";

    /// <summary>Only companies whose latest report covers a year that ended within this many months.</summary>
    [Range(6, 60)] public int RecentMonths { get; set; } = 24;
    /// <summary>Reports read per company: the latest gives two years, each older one a year more.</summary>
    [Range(1, 4)] public int ReportsPerCompany { get; set; } = 2;
    /// <summary>Report links tried per company before giving up (a link can be a summary or unreadable).</summary>
    [Range(1, 10)] public int MaxReportTries { get; set; } = 4;
    /// <summary>Companies read at the same time (each one's website is read one request at a time).</summary>
    [Range(1, 16)] public int Parallelism { get; set; } = 6;
    /// <summary>Largest report downloaded, in MB.</summary>
    [Range(5, 500)] public int MaxReportMegabytes { get; set; } = 80;
    /// <summary>Import only these tickers (testing, e.g. "BXB", "AIR"); empty = every listed company.</summary>
    public List<string> OnlyTickers { get; set; } = [];

    /// <summary>Named areas for the report; a region with WholeCountry takes everything else in its country ("AU", "NZ").</summary>
    public List<RegionOptions> Regions { get; set; } = [];

    /// <summary>Reviewable list of the companies (exchange, ticker, name, sector, website, LEI…); edit a website to fix it.</summary>
    public string ListPath { get; set; } = "data/curated/anz-companies.csv";
    public string ReportPath { get; set; } = "data/import-report-anz.md";
}
