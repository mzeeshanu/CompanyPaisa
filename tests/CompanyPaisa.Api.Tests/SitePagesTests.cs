using System.Net;
using CompanyPaisa.Api.Endpoints;
using CompanyPaisa.Client;
using CompanyPaisa.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyPaisa.Api.Tests;

/// <summary>The sample data with a stand-in for the built website (wwwroot/index.html).</summary>
public sealed class SitePagesFactory : WebApplicationFactory<Program>
{
    public const string IndexHtml = """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <meta name="description" content="Discover the public companies near you and explore their money." />
            <title>CompanyPaisa</title>
          </head>
          <body><div id="root"></div></body>
        </html>
        """;

    private readonly string _webRoot = Directory.CreateTempSubdirectory("companypaisa-web-").FullName;

    public SitePagesFactory() => File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "None");
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { }
    }
}

public class SitePagesTests(SitePagesFactory factory) : IClassFixture<SitePagesFactory>
{
    [Fact]
    public async Task A_company_page_carries_its_name_for_link_previews()
    {
        var res = await factory.CreateClient().GetAsync("/company/lfvn");
        var html = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>LifeVantage", html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/company/LFVN\" />", html);
        Assert.Contains("<meta property=\"og:title\" content=\"LifeVantage", html);
        Assert.Contains("<meta property=\"og:image\" content=\"http://localhost/og-image.png\" />", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\" />", html);
        Assert.Single(html.Split("name=\"description\"").Skip(1));   // replaced, not added twice
        // The facts are in the HTML itself, inside the app's root (the app replaces them when it starts).
        Assert.Contains("<div id=\"root\"><main class=\"ssr\"><h1>LifeVantage", html);
        Assert.Contains("<h2>Revenue and profit by year</h2>", html);
        Assert.Contains("<a href=\"/executive/", html);
        Assert.Contains("<script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@type\":\"Corporation\"", html);
        Assert.Contains("{\"@type\":\"ListItem\",\"position\":2,\"name\":\"LifeVantage", html);
    }

    [Fact]
    public async Task A_company_with_no_salaries_has_no_salaries_page()
    {
        // The sample data has no job salaries: the page says so with a 404 rather than an empty page.
        var res = await factory.CreateClient().GetAsync("/company/LFVN/salaries");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain("http://localhost/company/LFVN/salaries", await SitemapAddressesAsync(factory.CreateClient()));
    }

    [Fact]
    public async Task The_home_page_links_every_area_and_the_largest_companies()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("<h1>Public companies near you", html);
        Assert.Contains("<a href=\"/near/84043\">", html);
        Assert.Contains("<a href=\"/company/", html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/\" />", html);
        Assert.Contains("\"@type\":\"WebSite\",\"name\":\"CompanyPaisa\",\"url\":\"http://localhost/\"", html);   // the site name in results
        Assert.Contains("<a href=\"/near/united-states\">United States</a>", html);                        // every country and state with companies
        Assert.Contains("<a href=\"/near/utah\">Utah</a>", html);
    }

    [Fact]
    public async Task A_search_page_lists_the_companies_near_the_place()
    {
        var html = await factory.CreateClient().GetStringAsync("/near/84043");

        Assert.Contains("<h1>Public companies near Lehi, UT 84043</h1>", html);
        Assert.Contains("<a href=\"/company/LFVN\">", html);
    }

    [Fact]
    public async Task Page_text_is_encoded()
    {
        var html = SitePages.WithContent(SitePagesFactory.IndexHtml, new PageContent("<h1>A</h1>", new Dictionary<string, object?> { ["name"] = "</script><b>" }));

        Assert.Contains("<div id=\"root\"><main class=\"ssr\"><h1>A</h1></main></div>", html);
        Assert.DoesNotContain("</script><b>", html);
    }

    [Fact]
    public async Task An_executive_page_carries_the_persons_name_and_company()
    {
        var http = factory.CreateClient();
        var person = (await new CompanyPaisaClient(factory.CreateClient())
            .GetExecutivesNearAsync(new ExecutivesNearRequest { Near = "84043", RadiusMiles = 25 })).Items[0];

        var html = await http.GetStringAsync($"/executive/{person.PersonId}");

        Assert.Contains($"<title>{System.Text.Encodings.Web.HtmlEncoder.Default.Encode(person.Name)}", html);
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(person.Company.Name), html);
    }

    [Fact]
    public async Task Unknown_pages_still_load_the_app_with_a_404()
    {
        var res = await factory.CreateClient().GetAsync("/company/NOPE");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("<title>CompanyPaisa</title>", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", html);
    }

    [Fact]
    public async Task A_search_address_carries_the_place_for_link_previews()
    {
        var http = factory.CreateClient();

        var companies = await http.GetStringAsync("/near/84043?radius=25");
        var executives = await http.GetStringAsync("/near/84043/executives");
        var me = await http.GetStringAsync("/near/me");
        var unknown = await http.GetAsync("/near/00000");

        Assert.Contains("<title>Public companies near Lehi, UT 84043", companies);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/near/84043\" />", companies);
        Assert.Contains("<title>Executives and their pay near Lehi, UT 84043", executives);
        Assert.Contains("<title>Public companies near you", me);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", me);                  // different for every visitor
        Assert.DoesNotContain("noindex", companies);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", executives);
        Assert.Contains("\"name\":\"Near Lehi, UT 84043\",\"item\":\"http://localhost/near/84043\"", executives);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Companies_and_executives_can_be_searched_by_name()
    {
        var client = new CompanyPaisaClient(factory.CreateClient());
        var byTicker = await client.SearchByNameAsync("lfvn");
        var person = (await client.GetExecutivesNearAsync(new ExecutivesNearRequest { Near = "84043", RadiusMiles = 25 })).Items[0];
        var byName = await client.SearchByNameAsync(person.Name, limit: 3);

        Assert.Equal("LFVN", byTicker.Companies[0].Ticker);
        Assert.Equal("Lehi", byTicker.Companies[0].City);
        Assert.Contains(byName.Executives, e => e.PersonId == person.PersonId);
        Assert.True(byName.Executives.Count <= 3);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().GetAsync("/api/v1/search?q=")).StatusCode);
    }

    [Fact]
    public async Task A_company_has_insights_and_similar_companies()
    {
        var client = new CompanyPaisaClient(factory.CreateClient());

        var insights = await client.GetCompanyInsightsAsync("lfvn");

        Assert.NotNull(insights);
        Assert.Equal("LFVN", insights.Ticker);
        Assert.True(insights.RevenuePerSecond > 0);
        Assert.NotEmpty(insights.Similar);
        Assert.DoesNotContain(insights.Similar, s => s.Ticker == "LFVN");
        Assert.Null(await client.GetCompanyInsightsAsync("NOPE"));
    }

    [Fact]
    public async Task The_sitemap_lists_areas_companies_and_executives()
    {
        var http = factory.CreateClient();
        var index = System.Xml.Linq.XDocument.Parse(await http.GetStringAsync("/sitemap.xml"));
        var robots = await http.GetStringAsync("/robots.txt");

        Assert.Equal("sitemapindex", index.Root!.Name.LocalName);
        Assert.Equal("http://localhost/sitemap-1.xml", Locs(index).First());
        var locs = await SitemapAddressesAsync(http);
        Assert.Contains("http://localhost/", locs);
        Assert.Contains("http://localhost/near/84043", locs);
        Assert.Contains("http://localhost/near/FR-75008", locs);
        Assert.Contains("http://localhost/company/LFVN", locs);
        Assert.Contains(locs, l => l.StartsWith("http://localhost/executive/"));
        Assert.Equal(locs.Count, locs.Distinct().Count());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/sitemap-{Locs(index).Count + 1}.xml")).StatusCode);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
        Assert.Contains("Disallow: /admin", robots);
    }

    /// <summary>Every address in the sitemap: the index, then each file it lists.</summary>
    private static async Task<List<string>> SitemapAddressesAsync(HttpClient http)
    {
        var all = new List<string>();
        foreach (var file in Locs(System.Xml.Linq.XDocument.Parse(await http.GetStringAsync("/sitemap.xml"))))
        {
            var doc = System.Xml.Linq.XDocument.Parse(await http.GetStringAsync(new Uri(file).PathAndQuery));
            Assert.Equal("urlset", doc.Root!.Name.LocalName);
            var locs = Locs(doc);
            Assert.InRange(locs.Count, 1, SitePages.SitemapFileSize);
            all.AddRange(locs);
        }
        return all;
    }

    private static List<string> Locs(System.Xml.Linq.XDocument doc) =>
        doc.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();

    [Theory]
    [InlineData("84043", "US", "84043")]
    [InlineData("M5J 2J2", "CA", "M5J2J2")]
    [InlineData("sw1a 1aa", "UK", "SW1A1AA")]
    [InlineData("75008", "FR", "FR-75008")]
    [InlineData("1012 AB", "NL", "NL-1012AB")]
    public void Places_in_addresses_have_no_spaces_and_keep_their_country(string postcode, string country, string expected) =>
        Assert.Equal(expected, SitePages.PlaceToken(postcode, country));

    [Fact]
    public void Titles_are_html_encoded()
    {
        var html = SitePages.WithMeta(SitePagesFactory.IndexHtml, new PageMeta("AT&T <Inc>", "\"Quotes\"", "/company/T"), "https://companypaisa.com");

        Assert.Contains("<title>AT&amp;T &lt;Inc&gt;</title>", html);
        Assert.Contains("content=\"&quot;Quotes&quot;\"", html);
    }

    [Fact]
    public async Task A_company_page_links_its_salaries_and_its_state_and_country()
    {
        var html = await factory.CreateClient().GetStringAsync("/company/LFVN");

        Assert.Contains("<a href=\"/near/utah\">in Utah</a>", html);
        Assert.Contains("<a href=\"/near/united-states\">in the United States</a>", html);
    }

    [Theory]
    [InlineData("/near/Utah", "/near/utah")]
    [InlineData("/near/US-UT/executives", "/near/utah/executives")]
    [InlineData("/near/ut", "/near/utah")]
    [InlineData("/near/84043-1234?radius=25", "/near/84043?radius=25")]
    public async Task Each_place_has_one_address(string asked, string moved)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await http.GetAsync(asked);

        Assert.Equal(HttpStatusCode.MovedPermanently, res.StatusCode);
        Assert.Equal(moved, res.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(moved)).StatusCode);   // and the address it moves to is the page itself
    }
}

/// <summary>The site with its public address set, reached on another host (the platform's own address) and on its own.</summary>
public sealed class PublicHostFactory : WebApplicationFactory<Program>
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("companypaisa-web-").FullName;

    public PublicHostFactory() => File.WriteAllText(Path.Combine(_webRoot, "index.html"), SitePagesFactory.IndexHtml);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "None");
        builder.UseSetting("Hosting:PublicOrigin", "https://companypaisa.com");
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { }
    }
}

public class PublicHostTests(PublicHostFactory factory) : IClassFixture<PublicHostFactory>
{
    [Fact]
    public async Task Another_host_moves_to_the_public_address_except_the_health_check()
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var page = await http.GetAsync("/company/LFVN?x=1");
        Assert.Equal(HttpStatusCode.MovedPermanently, page.StatusCode);
        Assert.Equal("https://companypaisa.com/company/LFVN?x=1", page.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task On_the_public_address_pages_name_it_as_canonical()
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://companypaisa.com"), AllowAutoRedirect = false });
        var res = await http.GetAsync("/company/LFVN");
        var sitemap = await http.GetStringAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<link rel=\"canonical\" href=\"https://companypaisa.com/company/LFVN\" />", await res.Content.ReadAsStringAsync());
        Assert.Contains("<loc>https://companypaisa.com/sitemap-1.xml</loc>", sitemap);
    }
}
