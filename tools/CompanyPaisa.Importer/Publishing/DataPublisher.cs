using System.Globalization;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Publishing;

/// <summary>
/// Publishes one market's rows into the website's database (Importer:Output:DatabasePath). Every importer ends here, so
/// they share one output format and one safety net: the new file is read back and checked with the API's own rules
/// before it replaces the old one, and other markets' rows are left untouched.
/// </summary>
public sealed class DataPublisher(IOptions<ImporterOptions> options, RepoPaths paths, ILogger<DataPublisher> logger)
{
    public string DatabasePath => paths.Resolve(options.Value.Output.DatabasePath);

    /// <param name="market">The importer run's market id: "sec" (US, Canada, Australia…), "uk", "eu".</param>
    /// <param name="source">Where the figures come from, stored with the market's metadata.</param>
    /// <param name="region">The areas covered, for the metadata.</param>
    public void Publish(string market, IReadOnlyList<Company> companies, IReadOnlyList<CompanyLocation> locations,
        IReadOnlyList<FinancialPeriod> financials, IReadOnlyList<ExecutiveCompensation> pay, IReadOnlyList<Person> people,
        IReadOnlyList<NewExecutive> newExecutives, string source, string region)
    {
        var now = DateTime.UtcNow;
        var meta = new Dictionary<string, string>
        {
            ["data_version"] = $"{market}-{now:yyyy.MM.dd}",
            ["as_of_date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["is_sample"] = "false",
            ["source"] = source,
            ["region"] = region
        };
        var data = new CompanyData(companies, locations, financials, pay, people,
            new DataSetMetadata(meta["data_version"], DateOnly.FromDateTime(now), false, now), newExecutives);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SqliteDataStore.ReplaceMarket(DatabasePath, market, data, meta);
        logger.LogInformation("Published market '{Market}' ({Companies} companies, {Periods} periods, {Pay} pay rows) to {Path}, checked with the API's rules, in {Seconds:0.0}s",
            market, companies.Count, financials.Count, pay.Count, DatabasePath, watch.Elapsed.TotalSeconds);
    }
}
