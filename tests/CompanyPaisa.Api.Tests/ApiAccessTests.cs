using System.Net;
using System.Text.Json.Nodes;
using CompanyPaisa.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyPaisa.Api.Tests;

/// <summary>The site as it runs in production: no anonymous API access, only keys and the website's own pages.</summary>
public sealed class LockedApiFactory : WebApplicationFactory<Program>
{
    public const string Key = "test-key-0123456789abcdef";
    private readonly string _webRoot = Directory.CreateTempSubdirectory("companypaisa-web-").FullName;

    public LockedApiFactory() => File.WriteAllText(Path.Combine(_webRoot, "index.html"), SitePagesFactory.IndexHtml);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "None");
        builder.UseSetting("Api:AllowAnonymous", "false");
        builder.UseSetting("Api:SiteSession:Secret", "a-test-secret-that-is-long-enough-000");
        builder.UseSetting("Api:Keys:0:Name", "partner");
        builder.UseSetting("Api:Keys:0:Key", Key);
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { }
    }
}

public class ApiAccessTests(LockedApiFactory factory) : IClassFixture<LockedApiFactory>
{
    private const string Browser = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    /// <summary>A browser that has opened one of the site's pages (so it holds the site pass).</summary>
    private async Task<HttpClient> VisitorAsync()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
        var page = await http.GetAsync("/company/LFVN");
        Assert.Contains(page.Headers.GetValues("Set-Cookie"), c => c.StartsWith("cp_site=") && c.Contains("httponly") && c.Contains("path=/api"));
        return http;
    }

    private static HttpRequestMessage Get(string url, string? fetchSite = "same-origin")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (fetchSite is not null) request.Headers.Add("Sec-Fetch-Site", fetchSite);
        return request;
    }

    [Fact]
    public async Task Without_a_key_or_the_sites_pass_the_API_says_no()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);
        var res = await http.GetAsync("/api/v1/companies/LFVN");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal(ApiKeyEndpointFilter.SessionRequired, JsonNode.Parse(await res.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_app_with_a_key_gets_in()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", LockedApiFactory.Key);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.9");   // a key beats the bot rules

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/companies/LFVN")).StatusCode);
    }

    [Fact]
    public async Task The_sites_own_pages_get_in_with_their_pass()
    {
        var http = await VisitorAsync();

        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Get("/api/v1/companies/LFVN"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Get("/api/v1/meta", fetchSite: null))).StatusCode);   // an older browser
    }

    [Fact]
    public async Task Another_websites_page_cant_use_the_pass()
    {
        var http = await VisitorAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(Get("/api/v1/companies/LFVN", "cross-site"))).StatusCode);
    }

    [Fact]
    public async Task Scripts_are_turned_away_even_with_the_pass()
    {
        var http = await VisitorAsync();
        var request = Get("/api/v1/companies/LFVN");
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", "python-requests/2.32");

        Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_forged_pass_is_refused()
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = Get("/api/v1/companies/LFVN");
        request.Headers.TryAddWithoutValidation("User-Agent", Browser);
        request.Headers.Add("Cookie", $"cp_site={DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}.AAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_page_left_open_can_renew_its_pass()
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);

        var renewed = await http.SendAsync(Get("/api/session"));
        Assert.Equal(HttpStatusCode.NoContent, renewed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(Get("/api/v1/companies/LFVN"))).StatusCode);

        var script = factory.CreateClient();
        script.DefaultRequestHeaders.UserAgent.ParseAdd("Go-http-client/2.0");
        Assert.Equal(HttpStatusCode.Forbidden, (await script.SendAsync(Get("/api/session"))).StatusCode);
    }

    [Fact]
    public async Task Crawlers_are_told_to_keep_out_of_the_API()
    {
        var robots = await factory.CreateClient().GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /api/", robots);
    }
}
