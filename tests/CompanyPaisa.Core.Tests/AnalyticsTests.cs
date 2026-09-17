using CompanyPaisa.Analytics;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Features.Companies;
using CompanyPaisa.Core.Features.Search;
using CompanyPaisa.Infrastructure.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompanyPaisa.Core.Tests;

public class AnalyticsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1", "Phone", "Safari", "iOS", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36 Edg/139.0.0.0", "Desktop", "Edge", "Windows", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 15; SM-S928B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/28.0 Chrome/130.0.0.0 Mobile Safari/537.36", "Phone", "Samsung Internet", "Android", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36", "Tablet", "Chrome", "Android", false)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.6; rv:141.0) Gecko/20100101 Firefox/141.0", "Desktop", "Firefox", "macOS", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Desktop", "Other", "Other", true)]
    [InlineData("curl/8.9.1", "Desktop", "Other", "Other", true)]
    [InlineData("", "Unknown", "Unknown", "Unknown", true)]
    public void Reads_device_browser_and_os_and_spots_bots(string ua, string device, string browser, string os, bool bot) =>
        Assert.Equal(new UserAgentInfo(device, browser, os, bot), UserAgentInfo.Parse(ua));

    [Fact]
    public void Railway_postgres_urls_become_npgsql_connection_strings()
    {
        var cs = PostgresConnectionString.From("postgresql://postgres:p%40ss@postgres.railway.internal:5432/railway?sslmode=require");
        Assert.Contains("Host=postgres.railway.internal", cs);
        Assert.Contains("Username=postgres", cs);
        Assert.Contains("Password=p@ss", cs);
        Assert.Contains("Database=railway", cs);
        Assert.Contains("SSL Mode=Require", cs);
        Assert.Equal("Host=h;Database=d", PostgresConnectionString.From("Host=h;Database=d"));
    }

    [Fact]
    public void Visitor_hash_is_stable_within_a_day_and_changes_with_the_salt()
    {
        var today = AnalyticsWriter.VisitorHash("salt-1", "1.2.3.4|Chrome");
        Assert.Equal(today, AnalyticsWriter.VisitorHash("salt-1", "1.2.3.4|Chrome"));
        Assert.NotEqual(today, AnalyticsWriter.VisitorHash("salt-2", "1.2.3.4|Chrome"));
        Assert.Equal(32, today.Length);
        Assert.DoesNotContain("1.2.3.4", today);
    }

    [Fact]
    public async Task Sqlite_store_writes_events_and_builds_the_report()
    {
        var path = Path.Combine(Path.GetTempPath(), $"analytics-test-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteAnalyticsStore(path);
            await store.InitializeAsync(CancellationToken.None);
            var day = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
            AnalyticsEvent E(string name, string visitor, string? subject = null, string? label = null, double? lat = null, string? city = null, string? referrer = null, int dayOffset = 0) =>
                new(day.AddDays(dayOffset), name, visitor, subject, label, null, lat, lat is null ? null : -111.85, "US", "Utah", city, "Phone", "Safari", "iOS", referrer, "/", "website");

            await store.WriteAsync([
                E("page_view", "v1", city: "Lehi", referrer: "google.com"),
                E("page_view", "v2", city: "Provo"),
                E("search", "v1", lat: 40.39, label: "Lehi, UT"),
                E("search", "v2", lat: 40.39),
                E("company_view", "v1", "ADBE", "Adobe Inc."),
                E("company_view", "v1", "ADBE", "Adobe Inc."),
                E("company_view", "v2", "ADBE", "Adobe Inc."),
                E("executive_view", "v2", "p-1", "Jane Doe (Adobe Inc.)"),
                E("page_view", "v9", dayOffset: -40),   // outside the range
            ], CancellationToken.None);

            var report = await store.GetReportAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16), 10, CancellationToken.None);

            Assert.Equal(new AnalyticsTotals(Visitors: 2, PageViews: 2, Searches: 2, CompanyViews: 3, ExecutiveViews: 1, Events: 8), report.Totals);
            Assert.Equal(new AnalyticsDay("2026-09-16", 2, 2, 2, 3, 1), Assert.Single(report.Days));
            Assert.Equal(new AnalyticsCount("ADBE", "Adobe Inc.", 3, 2), Assert.Single(report.Companies));
            Assert.Equal(("40.39, -111.85", 2, 2), (report.SearchPoints[0].Key, report.SearchPoints[0].Count, report.SearchPoints[0].Visitors));
            Assert.Contains(report.Cities, c => c.Key == "Lehi, Utah, US");
            Assert.Equal("google.com", Assert.Single(report.Referrers).Key);
            Assert.Equal(8, await store.GetEventsAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 16), CancellationToken.None).CountAsync());

            // One salt per day; the day before yesterday's is gone once a new day starts.
            var salt = await store.GetDailySaltAsync("2026-09-14", CancellationToken.None);
            Assert.Equal(salt, await store.GetDailySaltAsync("2026-09-14", CancellationToken.None));
            await store.GetDailySaltAsync("2026-09-16", CancellationToken.None);
            Assert.NotEqual(salt, await store.GetDailySaltAsync("2026-09-14", CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) try { File.Delete(f); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Behavior_records_successful_tracked_requests_only()
    {
        var tracker = new RecordingTracker();
        var behavior = new AnalyticsBehavior<GetCompanyQuery, CompanyDetailDto>(tracker, NullLogger<AnalyticsBehavior<GetCompanyQuery, CompanyDetailDto>>.Instance);
        var company = new CompanyDetailDto("ADBE", "Adobe Inc.", "NASDAQ", "Software", null, null, null, null, null, "USD", null, null, [],
            new CompanyIndicatorsDto(0, 0, null, null, 5, null, TrendStatus.Flat, null, null, null, []));

        await behavior.HandleAsync(new GetCompanyQuery("adbe"), () => Task.FromResult(company), CancellationToken.None);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            behavior.HandleAsync(new GetCompanyQuery("nope"), () => throw new NotFoundException("no"), CancellationToken.None));

        Assert.Equal(new AnalyticsAction("company_view", "ADBE", "Adobe Inc."), Assert.Single(tracker.Actions));
    }

    [Fact]
    public void Searches_are_recorded_at_about_1_km_and_the_location_screen_check_is_not()
    {
        var response = new NearbyCompaniesResponse(new GeoPointDto(40.3916, -111.8508), null, 10, CompanySort.Revenue, 1, 50, 12,
            new NearbySummaryDto(12, 0, 0, 0), []);

        var action = new GetCompaniesNearQuery(new NearbyCompaniesRequest { Latitude = 40.3916, Longitude = -111.8508, RadiusMiles = 10 }).Describe(response);
        Assert.Equal(("search", 40.39, -111.85), (action!.Name, action.Latitude, action.Longitude));
        Assert.Equal("12", action.Detail!["results"]);

        Assert.Null(new GetCompaniesNearQuery(new NearbyCompaniesRequest { Latitude = 40.39, Longitude = -111.85, PageSize = 1 }).Describe(response));
        Assert.Null(new GetCompaniesNearQuery(new NearbyCompaniesRequest { Latitude = 40.39, Longitude = -111.85, Page = 2 }).Describe(response));
    }

    private sealed class RecordingTracker : IAnalyticsTracker
    {
        public List<AnalyticsAction> Actions { get; } = [];
        public void Track(AnalyticsAction action) => Actions.Add(action);
    }
}
