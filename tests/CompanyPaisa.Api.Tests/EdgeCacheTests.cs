using System.Net;
using System.Text;
using CompanyPaisa.Api.Composition;
using CompanyPaisa.EdgeCache;
using CompanyPaisa.EdgeCache.Cloudflare;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace CompanyPaisa.Api.Tests;

/// <summary>After a deploy the app empties the CDN's copy of the pages, so new data shows at once.</summary>
public class EdgeCacheTests
{
    private sealed class Recorder(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly CloudflareEdgeCacheOptions Zone = new() { ZoneId = "zone123", ApiToken = "token-abc" };

    [Fact]
    public async Task Cloudflare_is_asked_to_purge_everything_on_the_zone()
    {
        var handler = new Recorder(HttpStatusCode.OK, """{"success":true,"errors":[],"result":{"id":"zone123"}}""");

        await new CloudflareEdgeCache(new HttpClient(handler), Zone).PurgeAllAsync(CancellationToken.None);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.cloudflare.com/client/v4/zones/zone123/purge_cache", request.RequestUri!.ToString());
        Assert.Equal("Bearer token-abc", request.Headers.Authorization!.ToString());
        Assert.Equal("""{"purge_everything":true}""", body);
    }

    [Fact]
    public async Task A_refused_purge_says_why_without_the_token()
    {
        var handler = new Recorder(HttpStatusCode.Forbidden, """{"success":false,"errors":[{"code":10000,"message":"Authentication error"}]}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CloudflareEdgeCache(new HttpClient(handler), Zone).PurgeAllAsync(CancellationToken.None));

        Assert.Contains("10000 Authentication error", ex.Message);
        Assert.DoesNotContain("token-abc", ex.Message);
    }

    private sealed class FlakyCache(int failures) : IEdgeCache
    {
        public int Attempts { get; private set; }
        public string Name => "Test";

        public Task PurgeAllAsync(CancellationToken ct) =>
            ++Attempts <= failures ? Task.FromException(new HttpRequestException("down")) : Task.CompletedTask;
    }

    private static EdgeCachePurgeOnStart Purger(IEdgeCache cache) => new(cache, new EdgeCacheStatus("Test", true, ""),
        Microsoft.Extensions.Options.Options.Create(new EdgeCacheOptions()), new Lifetime(), NullLogger<EdgeCachePurgeOnStart>.Instance,
        retries: [TimeSpan.Zero, TimeSpan.Zero]);

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    [Fact]
    public async Task A_failed_purge_is_tried_again()
    {
        var cache = new FlakyCache(failures: 2);
        var purger = Purger(cache);

        await purger.PurgeAsync(CancellationToken.None);

        Assert.True(purger.Purged);
        Assert.Equal(3, cache.Attempts);
    }

    [Fact]
    public async Task A_purge_that_keeps_failing_gives_up_without_stopping_the_site()
    {
        var cache = new FlakyCache(failures: 10);
        var purger = Purger(cache);

        await purger.PurgeAsync(CancellationToken.None);   // no exception

        Assert.False(purger.Purged);
        Assert.Equal(3, cache.Attempts);
    }

    /// <summary>The services the site registers for these EdgeCache settings, with the adapters it ships.</summary>
    private static (ServiceProvider Services, EdgeCacheStatus Status) Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>($"EdgeCache:{s.Key}", s.Value))).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IHostApplicationLifetime, Lifetime>();
        var status = services.AddCompanyPaisaEdgeCache(configuration, ApiServiceCollectionExtensions.AddEdgeCacheAdapters);
        return (services.BuildServiceProvider(), status);
    }

    [Fact]
    public void Cloudflare_is_used_only_with_a_zone_and_a_token()
    {
        var (none, noneStatus) = Build();
        var (incomplete, incompleteStatus) = Build(("Provider", "Cloudflare"), ("Cloudflare:ZoneId", "zone123"));
        var (complete, completeStatus) = Build(("Provider", " cloudflare "), ("Cloudflare:ZoneId", "zone123"), ("Cloudflare:ApiToken", "token-abc"));
        using var _ = none; using var __ = incomplete; using var ___ = complete;

        Assert.Equal((EdgeCacheAdapters.None, false), (noneStatus.Provider, noneStatus.Enabled));
        Assert.Null(none.GetService<IEdgeCache>());
        Assert.Empty(none.GetServices<IHostedService>());

        // Named but incomplete: the site runs, and the purge service is there only to say why it won't purge.
        Assert.Equal(("Cloudflare", false), (incompleteStatus.Provider, incompleteStatus.Enabled));
        Assert.Contains("ApiToken", incompleteStatus.Reason);
        Assert.Null(incomplete.GetService<IEdgeCache>());
        Assert.Single(incomplete.GetServices<IHostedService>(), s => s is EdgeCachePurgeOnStart);

        Assert.True(completeStatus.Enabled);
        Assert.IsType<CloudflareEdgeCache>(complete.GetService<IEdgeCache>());
        Assert.Single(complete.GetServices<IHostedService>(), s => s is EdgeCachePurgeOnStart);
    }

    [Fact]
    public void An_unknown_provider_stops_startup_and_lists_the_known_ones()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(("Provider", "Fastly")));

        Assert.Contains("'Fastly'", ex.Message);
        Assert.Contains("None, Cloudflare", ex.Message);
    }

    private sealed class TestAdapter(string name) : IEdgeCacheAdapter
    {
        public string Name => name;
        public string? Problem(IConfigurationSection settings) => null;
        public IEdgeCache Create(IServiceProvider services, IConfigurationSection settings) => new FlakyCache(0);
    }

    [Fact]
    public void A_new_provider_is_one_registration_and_None_is_reserved()
    {
        var adapters = new EdgeCacheAdapters().AddCloudflare().Add(new TestAdapter("Bunny"));

        Assert.Equal(["None", "Bunny", "Cloudflare"], adapters.Names);
        Assert.Equal("Bunny", adapters.Get("bunny")!.Name);
        Assert.Null(adapters.Get(""));
        Assert.Throws<ArgumentException>(() => adapters.Add(new TestAdapter("none")));
    }
}
