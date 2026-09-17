using CompanyPaisa.Data.Excel;
using CompanyPaisa.Data.Sqlite;
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
        builder.UseSetting("DataSource:Provider", "Excel");
        builder.UseSetting("DataSource:Excel:Path", "../../data/sample/companypaisa.sample.xlsx");
        builder.UseSetting("DataSource:Excel:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "None");
    }
}

/// <summary>The same sample data, published into a SQLite database the way the importers publish the real data.</summary>
public sealed class SqliteSampleDataFactory : WebApplicationFactory<Program>
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"companypaisa-test-{Guid.NewGuid():N}.db");

    public SqliteSampleDataFactory()
    {
        var sample = Path.Combine(RepoRoot(), "data", "sample", "companypaisa.sample.xlsx");
        var data = ExcelWorkbookWriter.Read(sample);
        SqliteDataStore.ReplaceMarket(_database, "sample", data, new Dictionary<string, string>
        {
            ["data_version"] = data.Metadata.DataVersion, ["as_of_date"] = data.Metadata.AsOfDate?.ToString("yyyy-MM-dd") ?? "",
            ["is_sample"] = data.Metadata.IsSampleData ? "true" : "false"
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataSource:Provider", "Sqlite");
        builder.UseSetting("DataSource:Sqlite:Path", _database);
        builder.UseSetting("DataSource:Sqlite:ReloadOnChange", "false");
        builder.UseSetting("Geo:ZipTablePath", "../../data/reference/us-zip-centroids.sample.csv");
        builder.UseSetting("Analytics:Provider", "None");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_database); } catch (IOException) { }
    }

    /// <summary>From this source file's location (tests may be built outside the repository).</summary>
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
