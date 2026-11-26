using System.Net;
using CompanyPaisa.Infrastructure.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Tests;

/// <summary>The app with appsettings.Production.json, as on Railway (the sample data, and no redirect to the public host).</summary>
public sealed class ProductionFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Hosting:PublicOrigin", "");
        builder.UseSetting("Hosting:UseHttpsRedirection", "false");
    }
}

/// <summary>The production trims (CP-16): a smaller cache, no file watcher, no Swagger page.</summary>
public class ProductionSettingsTests(ProductionFactory factory) : IClassFixture<ProductionFactory>
{
    [Fact]
    public void The_cache_is_smaller_and_the_data_file_isnt_watched()
    {
        Assert.Equal(5000, factory.Services.GetRequiredService<IOptions<CachingOptions>>().Value.MaxEntries);
        Assert.Equal("False", factory.Services.GetRequiredService<IConfiguration>()["DataSource:Sqlite:ReloadOnChange"], ignoreCase: true);
    }

    [Fact]
    public async Task There_is_no_swagger_page_but_the_openapi_document_stays()
    {
        var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
