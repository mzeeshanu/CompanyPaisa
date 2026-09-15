using System.Globalization;
using System.Text;
using System.Text.Json;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Validation;

/// <summary>
/// --validate: loads every published workbook the way the API does, runs <see cref="DataValidator"/> over all of them
/// together and writes data/validation-report.md. With --strict the exit code is 1 when any error-level check fails.
/// </summary>
public sealed class ValidationRun(IOptions<ImporterOptions> options, RepoPaths paths, ILogger<ValidationRun> logger)
{
    public const string ReportPath = "data/validation-report.md";
    private const string ApiSettings = "src/CompanyPaisa.Api/appsettings.json";

    public async Task<int> RunAsync(bool strict, CancellationToken ct)
    {
        var o = options.Value;
        var workbooks = new[] { o.Output.WorkbookPath, o.Uk.WorkbookPath, o.Eu.WorkbookPath }.Select(paths.Resolve).Where(File.Exists).ToList();
        var parts = workbooks.Select(ExcelWorkbookWriter.Read).ToList();
        var all = new WorkbookContents(
            parts.SelectMany(p => p.Companies).ToList(), parts.SelectMany(p => p.Locations).ToList(),
            parts.SelectMany(p => p.Financials).ToList(), parts.SelectMany(p => p.Pay).ToList(),
            parts.SelectMany(p => p.People).DistinctBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase).ToList(), parts[0].Metadata);

        var result = DataValidator.Validate(all, ReadRates(), DateOnly.FromDateTime(DateTime.UtcNow));
        await File.WriteAllTextAsync(paths.Resolve(ReportPath), Render(result, workbooks), ct);

        foreach (var c in result.Checks.Where(c => c.Count > 0))
            logger.Log(c.Severity == Severity.Error ? LogLevel.Error : LogLevel.Warning, "{Area} · {Name}: {Count}", c.Area, c.Name, c.Count);
        logger.LogInformation("Validated {Companies} companies from {Workbooks} workbooks: {Errors} errors, {Warnings} warnings → {Report}",
            all.Companies.Count, workbooks.Count, result.Errors, result.Warnings, ReportPath);
        return strict && result.Errors > 0 ? 1 : 0;
    }

    /// <summary>The website's own exchange rates, so "large" means the same thing here and on the site.</summary>
    private Dictionary<string, decimal> ReadRates()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(paths.Resolve(ApiSettings)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return doc.RootElement.GetProperty("Currency").GetProperty("UsdPer").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetDecimal(), StringComparer.OrdinalIgnoreCase);
    }

    private static string Render(ValidationResult r, IReadOnlyList<string> workbooks)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Data validation report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("Checks every published workbook together, as the website sees it: ")
          .AppendLine(string.Join(", ", workbooks.Select(w => $"`{Path.GetFileName(w)}`")) + ".").AppendLine();
        sb.AppendLine("**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real " +
                      "(big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.").AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Errors: **{r.Errors}**").AppendLine(CultureInfo.InvariantCulture, $"- Warnings: **{r.Warnings}**").AppendLine();

        sb.AppendLine("## Checks").AppendLine().AppendLine("| Area | Check | Level | Rows |").AppendLine("|---|---|---|---|");
        foreach (var c in r.Checks) sb.AppendLine(CultureInfo.InvariantCulture, $"| {c.Area} | {c.Name} | {(c.Severity == Severity.Error ? "Error" : "Warning")} | {(c.Count == 0 ? "✓ 0" : c.Count.ToString("N0", CultureInfo.InvariantCulture))} |");
        sb.AppendLine();

        sb.AppendLine("## Overview").AppendLine();
        foreach (var t in r.Tallies)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {t.Title}").AppendLine().AppendLine("| | Count |").AppendLine("|---|---|");
            foreach (var (key, count) in t.Rows) sb.AppendLine(CultureInfo.InvariantCulture, $"| {key} | {count:N0} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Details").AppendLine();
        foreach (var c in r.Checks.Where(c => c.Count > 0))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {c.Area} · {c.Name} ({c.Count:N0})").AppendLine().AppendLine(c.Explanation).AppendLine();
            foreach (var e in c.Examples) sb.AppendLine($"- {e}");
            if (c.Count > c.Examples.Count) sb.AppendLine(CultureInfo.InvariantCulture, $"- … and {c.Count - c.Examples.Count:N0} more");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
