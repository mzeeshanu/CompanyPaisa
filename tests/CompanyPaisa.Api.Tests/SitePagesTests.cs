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
    public void Titles_are_html_encoded()
    {
        var html = SitePages.WithMeta(SitePagesFactory.IndexHtml, new PageMeta("AT&T <Inc>", "\"Quotes\"", "/company/T"), "https://companypaisa.com");

        Assert.Contains("<title>AT&amp;T &lt;Inc&gt;</title>", html);
        Assert.Contains("content=\"&quot;Quotes&quot;\"", html);
    }
}
