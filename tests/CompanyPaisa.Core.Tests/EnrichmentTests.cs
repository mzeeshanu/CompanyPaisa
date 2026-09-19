using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Enrichment;

namespace CompanyPaisa.Core.Tests;

/// <summary>Websites, careers pages and street positions: the rules that decide what's kept.</summary>
public class EnrichmentTests
{
    [Theory]
    [InlineData("Careers", "https://www.example.com/careers", true)]
    [InlineData("Join our team", "https://www.example.com/about/team", true)]
    [InlineData("<span>Jobs</span>", "https://example.wd5.myworkdayjobs.com/External", true)]
    [InlineData("", "https://careers.example.com/", false)]                       // no words at all: not enough
    [InlineData("Beware of recruitment scams", "https://www.example.com/careers/fraud", false)]
    [InlineData("Jobs report shows growth", "https://www.example.com/news/jobs-report", false)]
    [InlineData("Investors", "https://www.example.com/investors", false)]
    public void Scores_careers_links(string text, string url, bool isCareers) =>
        Assert.Equal(isCareers, CareersFinder.Score(text, new Uri(url)) > 0);

    [Fact]
    public void Follows_robots_txt()
    {
        var robots = Robots.Parse("""
            User-agent: *
            Disallow: /private
            Allow: /private/careers

            User-agent: BadBot
            Disallow: /
            """, "CompanyPaisa");

        Assert.True(robots.Allows("/"));
        Assert.True(robots.Allows("/careers"));
        Assert.False(robots.Allows("/private/data"));
        Assert.True(robots.Allows("/private/careers"));
        Assert.False(Robots.Parse("User-agent: CompanyPaisa\nDisallow: /", "CompanyPaisa").Allows("/"));
    }

    [Theory]
    [InlineData("https://ir.53.com/investors", "https://www.53.com")]
    [InlineData("www.lucky-cement.com", "https://www.lucky-cement.com")]
    [InlineData("http://www.example.co.uk/en/home", "http://www.example.co.uk")]
    public void Tidies_websites_to_the_home_page(string raw, string tidy) => Assert.Equal(tidy, WebsiteFinder.Tidy(raw));

    [Fact]
    public void Takes_the_website_a_filing_names_only_when_it_looks_like_the_company()
    {
        const string proxy = """
            <p>Our proxy materials are available at www.proxyvote.com and on our website at www.lifevantage.com/investors.</p>
            <p>See www.sec.gov. Contact Broadridge at www.broadridge.com. Visit https://www.lifevantage.com.</p>
            """;
        Assert.Equal("https://www.lifevantage.com", WebsiteFinder.FromFilingText(proxy, "LifeVantage Corporation", "LFVN"));
        Assert.Null(WebsiteFinder.FromFilingText("<p>Vote at www.proxyvote.com</p>", "LifeVantage Corporation", "LFVN"));
    }

    [Theory]
    [InlineData("tesco.com", "Tesco PLC", "TSCO.L", true)]
    [InlineData("ibm.com", "International Business Machines Corp", "IBM", true)]
    [InlineData("jpmorganchase.com", "JPMorgan Chase & Co", "JPM", true)]
    [InlineData("example.com", "Tesco PLC", "TSCO.L", false)]
    public void Matches_domains_to_company_names(string domain, string name, string ticker, bool matches) =>
        Assert.Equal(matches, WebsiteFinder.LooksLike(domain, name, ticker));

    [Fact]
    public void Applies_the_tables_only_while_the_address_is_unchanged()
    {
        var company = new Company { CompanyId = "X", Name = "X", Ticker = "X", Exchange = "X", Sector = "X" };
        var location = new CompanyLocation
        {
            LocationId = "X-HQ", CompanyId = "X", Type = LocationType.Headquarters, Label = "HQ", Street = "1 Main St", City = "Lehi", State = "UT",
            PostalCode = "84043", Point = new GeoPoint(40.39, -111.85)
        };
        var sites = new Dictionary<string, CompanySite> { ["X"] = new("X", "https://www.x.com", "wikidata", "https://www.x.com/careers", null) };
        var points = new Dictionary<string, GeocodedLocation> { ["X-HQ"] = new("X-HQ", EnrichmentTables.AddressOf(location), 40.43, -111.89, "us-census") };

        var (companies, locations) = EnrichmentTables.Apply([company], [location], sites, points);
        Assert.Equal("https://www.x.com/careers", companies[0].CareersUrl);
        Assert.Equal(40.43, locations[0].Point.Latitude);

        var moved = location with { Street = "2 Other Ave" };
        Assert.Equal(40.39, EnrichmentTables.Apply([company], [moved], sites, points).Locations[0].Point.Latitude);
    }

    [Fact]
    public void Reads_and_writes_the_tables()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sites-{Guid.NewGuid():N}.csv");
        try
        {
            EnrichmentTables.WriteSites(path, [new CompanySite("A, Inc", "https://a.com", "wikidata", null, new DateOnly(2026, 9, 19))]);
            var back = Assert.Single(EnrichmentTables.ReadSites(path).Values);
            Assert.Equal("A, Inc", back.CompanyId);
            Assert.Null(back.CareersUrl);
            Assert.Equal(new DateOnly(2026, 9, 19), back.CareersCheckedOn);
        }
        finally { File.Delete(path); }
    }
}
