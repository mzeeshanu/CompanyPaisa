using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyPaisa.Api.Tests;

/// <summary>
/// Hosts the API against the synthetic sample workbook, whatever dataset appsettings.json points at,
/// so tests don't change when the real SEC data is refreshed.
/// </summary>
public sealed class SampleDataFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
    }
}
