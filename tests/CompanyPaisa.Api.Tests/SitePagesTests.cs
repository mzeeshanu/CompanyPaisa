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
        Assert.Single(html.Split("name=\"description\"").Skip(1));   // replaced, not added twice
        Assert.Contains("<div id=\"root\"></div>", html);
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
        Assert.Contains("<title>CompanyPaisa</title>", await res.Content.ReadAsStringAsync());
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
        var sitemap = await http.GetStringAsync("/sitemap.xml");
        var robots = await http.GetStringAsync("/robots.txt");

        var doc = System.Xml.Linq.XDocument.Parse(sitemap);
        var locs = doc.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.Contains("http://localhost/", locs);
        Assert.Contains("http://localhost/near/84043", locs);
        Assert.Contains("http://localhost/near/FR-75008", locs);
        Assert.Contains("http://localhost/company/LFVN", locs);
        Assert.Contains(locs, l => l.StartsWith("http://localhost/executive/"));
        Assert.Equal(locs.Count, locs.Distinct().Count());
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
        Assert.Contains("Disallow: /admin", robots);
    }

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
}
