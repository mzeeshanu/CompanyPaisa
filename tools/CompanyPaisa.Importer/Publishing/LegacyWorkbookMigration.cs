using System.Globalization;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Publishing;

/// <summary>
/// --migrate-xlsx: copies the workbooks from before the move to SQLite (Importer:Output:LegacyWorkbooks, market → path)
/// into the database, one market each, keeping their data versions. Run once; the importers publish to the database after that.
/// </summary>
public sealed class LegacyWorkbookMigration(IOptions<ImporterOptions> options, RepoPaths paths, ILogger<LegacyWorkbookMigration> logger)
{
    public int Run()
    {
        var database = paths.Resolve(options.Value.Output.DatabasePath);
        foreach (var (market, relative) in options.Value.Output.LegacyWorkbooks)
        {
            var workbook = paths.Resolve(relative);
            if (!File.Exists(workbook)) { logger.LogWarning("{Market}: {Path} not found; skipped", market, workbook); continue; }
            var data = ExcelWorkbookWriter.Read(workbook);
            var meta = new Dictionary<string, string>
            {
                ["data_version"] = data.Metadata.DataVersion,
                ["as_of_date"] = data.Metadata.AsOfDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                ["is_sample"] = data.Metadata.IsSampleData ? "true" : "false",
                ["source"] = $"Migrated from {Path.GetFileName(workbook)}"
            };
            SqliteDataStore.ReplaceMarket(database, market, data, meta);
            logger.LogInformation("{Market}: {Companies} companies, {Periods} periods, {Pay} pay rows from {Workbook} → {Database}",
                market, data.Companies.Count, data.Financials.Count, data.Pay.Count, Path.GetFileName(workbook), database);
        }
        var size = new FileInfo(database).Length;
        logger.LogInformation("Database {Path}: {Size:0.0} MB", database, size / 1024.0 / 1024.0);
        return 0;
    }
}
