using System.Net;
using System.Net.Http.Json;
using CompanyPaisa.Api.Analytics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace CompanyPaisa.Api.Tests;

/// <summary>The sample data with analytics recording to a temporary SQLite file.</summary>
public sealed class AnalyticsFactory : WebApplicationFactory<Program>
{
    public const string DashboardKey = "test-dashboard-key-0001";
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"companypaisa-analytics-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "Sqlite");
        builder.UseSetting("Analytics:SqlitePath", _database);
        builder.UseSetting("Analytics:DashboardKey", DashboardKey);
        builder.UseSetting("Analytics:FlushIntervalMs", "20");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _database, _database + "-wal", _database + "-shm" }) try { File.Delete(f); } catch (IOException) { }
    }
}

public class AnalyticsApiTests(AnalyticsFactory factory) : IClassFixture<AnalyticsFactory>
{
    private const string Browser = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1";

    /// <summary>A visitor's browser on the website, behind Cloudflare in Lehi.</summary>
    private HttpClient Visitor(string userAgent = Browser)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        http.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        http.DefaultRequestHeaders.Add("CF-IPCountry", "US");
        http.DefaultRequestHeaders.Add("cf-region", "Utah");
        http.DefaultRequestHeaders.Add("cf-ipcity", "Lehi");
        return http;
    }

    private HttpClient Admin(string key = AnalyticsFactory.DashboardKey)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(AnalyticsEndpoints.AdminKeyHeader, key);
        return http;
    }

    /// <summary>Events are written in the background; wait until the report shows what the test expects.</summary>
    private async Task<AnalyticsDashboardResponse> ReportWhen(Func<AnalyticsDashboardResponse, bool> ready)
    {
        AnalyticsDashboardResponse? last = null;
        for (var i = 0; i < 100; i++)
        {
            last = await Admin().GetFromJsonAsync<AnalyticsDashboardResponse>("/api/admin/analytics/report?days=7");
            if (last is not null && ready(last)) return last;
            await Task.Delay(50);
        }
        Assert.Fail($"The report never showed the expected events. Last: {System.Text.Json.JsonSerializer.Serialize(last)}");
        return null!;
    }

    [Fact]
    public async Task Website_visits_searches_and_company_views_reach_the_dashboard()
    {
        var visitor = Visitor();
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsJsonAsync("/api/v1/events",
            new ClientEventRequest("page_view", Path: "/", Referrer: "https://www.google.com/search?q=companies"))).StatusCode);
        (await visitor.GetAsync("/api/v1/companies/near?latitude=40.3916&longitude=-111.8508&radiusMiles=10")).EnsureSuccessStatusCode();
        (await visitor.GetAsync("/api/v1/companies/near?latitude=40.3916&longitude=-111.8508&radiusMiles=60&pageSize=1")).EnsureSuccessStatusCode();
        (await visitor.GetAsync("/api/v1/companies/lfvn")).EnsureSuccessStatusCode();

        var data = await ReportWhen(d => d.Report?.Companies.Any(c => c.Key == "LFVN") == true && d.Report.Referrers.Count > 0);
        var report = data.Report!;

        Assert.True(data.Status.Enabled);
        Assert.Contains(report.Referrers, r => r.Key == "google.com");
        Assert.Contains(report.Cities, c => c.Key == "Lehi, Utah, US");
        Assert.Contains(report.Devices, d => d.Key == "Phone");
        Assert.Contains(report.Sources, s => s.Key == "website");
        Assert.Contains(data.Areas, a => a.Key.EndsWith(", UT"));
        // The location screen's one-row check isn't a search.
        Assert.DoesNotContain(await EventsCsv(), l => l.Contains("\"{\"\"radiusMiles\"\":\"\"60\"\""));
    }

    [Fact]
    public async Task Bots_and_the_owners_browser_are_not_counted()
    {
        (await Visitor("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)").GetAsync("/api/v1/companies/nus")).EnsureSuccessStatusCode();

        var owner = Visitor();
        (await owner.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/admin/analytics/owner")
            { Headers = { { AnalyticsEndpoints.AdminKeyHeader, AnalyticsFactory.DashboardKey } } })).EnsureSuccessStatusCode();
        (await owner.GetAsync("/api/v1/companies/usna")).EnsureSuccessStatusCode();

        (await Visitor().GetAsync("/api/v1/companies/zion")).EnsureSuccessStatusCode();   // a real visitor, so we know the writer has run
        var report = (await ReportWhen(d => d.Report?.Companies.Any(c => c.Key == "ZION") == true)).Report!;

        Assert.DoesNotContain(report.Companies, c => c.Key is "NUS" or "USNA");
    }

    [Fact]
    public async Task Unknown_client_events_are_rejected()
    {
        var res = await Visitor().PostAsJsonAsync("/api/v1/events", new ClientEventRequest("drop_table"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Dashboard_needs_the_key()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/admin/analytics/report")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Admin("wrong-key-wrong-key").GetAsync("/api/admin/analytics/report")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Admin().GetAsync("/api/admin/analytics/report")).StatusCode);
    }

    private async Task<List<string>> EventsCsv()
    {
        var csv = await Admin().GetStringAsync("/api/admin/analytics/events.csv?days=7");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.StartsWith("occurred_at,day,name,visitor", lines[0]);
        return lines;
    }
}
