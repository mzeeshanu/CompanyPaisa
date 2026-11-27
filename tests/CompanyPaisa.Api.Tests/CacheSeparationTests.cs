using CompanyPaisa.Core.Features.Companies;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyPaisa.Api.Tests;

/// <summary>The sample data with the smallest query cache allowed (10 rows): far smaller than the sitemap.</summary>
public sealed class SmallQueryCacheFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Caching:MaxEntries", "10");
    }
}

/// <summary>
/// Query results are limited in rows, in a cache of their own (CP-34). Sharing one size-limited cache meant the sitemap,
/// larger than the whole limit, was never kept and was rebuilt on every request.
/// </summary>
public class CacheSeparationTests(SmallQueryCacheFactory factory) : IClassFixture<SmallQueryCacheFactory>
{
    [Fact]
    public void Query_results_have_their_own_cache_limited_in_rows()
    {
        var queries = factory.Services.GetRequiredService<QueryResultCache>();
        var general = factory.Services.GetRequiredService<IMemoryCache>();

        Assert.NotSame(queries, general);
        Assert.Equal(10, queries.SizeLimit);
        // The general cache has no size limit, so its entries don't need a size (a limited cache throws without one).
        general.Set("cp34-test", 1);
        Assert.Equal(1, general.Get<int>("cp34-test"));
    }

    [Fact]
    public async Task The_sitemap_is_kept_even_when_it_is_larger_than_the_query_cache()
    {
        var http = factory.CreateClient();
        await http.GetStringAsync("/sitemap.xml");

        var general = (MemoryCache)factory.Services.GetRequiredService<IMemoryCache>();
        Assert.Contains(general.Keys, k => k is string s && s.StartsWith("sitemap:"));
    }

    [Fact]
    public async Task A_query_is_answered_from_the_query_cache()
    {
        using var scope = factory.Services.CreateScope();
        var requestor = scope.ServiceProvider.GetRequiredService<IServiceRequestor>();

        var first = await requestor.SendAsync(new GetCompanyQuery("LFVN"));
        var second = await requestor.SendAsync(new GetCompanyQuery("LFVN"));

        Assert.Same(first, second);
        Assert.True(factory.Services.GetRequiredService<QueryResultCache>().Count > 0);
    }
}
