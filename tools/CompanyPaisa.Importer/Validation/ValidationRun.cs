using System.Globalization;
using System.Text;
using System.Text.Json;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Validation;

/// <summary>
/// --validate: loads the published database the way the API does, runs <see cref="DataValidator"/> over every market
/// together, applies the reviewed decisions (<see cref="AuditLog"/>) and writes data/validation-report.md and the open
/// findings to data/audit-worklist.csv. With --strict the exit code is 1 when any open error-level finding remains.
/// </summary>
public sealed class ValidationRun(IOptions<ImporterOptions> options, RepoPaths paths, ILogger<ValidationRun> logger)
{
    public const string ReportPath = "data/validation-report.md";
    public const string EdgarReportPath = "data/edgar-crosscheck.md";
    public const string EdgarCacheDir = "data/cache/edgar";
    private const string ApiSettings = "src/CompanyPaisa.Api/appsettings.json";

    public async Task<int> RunAsync(bool strict, CancellationToken ct)
    {
        var database = paths.Resolve(options.Value.Output.DatabasePath);
        var all = SqliteDataStore.Read(database, DateTimeOffset.UtcNow);
        var markets = SqliteDataStore.Markets(database).Select(m => $"{m.Key} ({m.Value.GetValueOrDefault("data_version")})").ToList();

        var rates = ReadRates();
        var result = DataValidator.Validate(all, rates, DateOnly.FromDateTime(DateTime.UtcNow));

        // EdgarTools shadow comparison: only when tools/edgar/extract.py has written its files (warnings, never errors).
        var edgar = EdgarCrossCheck.Load(paths.Resolve(EdgarCacheDir));
        if (edgar.Count > 0)
        {
            var cross = EdgarCrossCheck.Compare(all, edgar, rates);
            result = result with { Checks = [.. result.Checks, .. cross.Checks] };
            await File.WriteAllTextAsync(paths.Resolve(EdgarReportPath), EdgarCrossCheck.Render(cross, all, rates), ct);
            logger.LogInformation("EdgarTools cross-check: {Reports} companies, {Compared} figures compared → {Report}",
                edgar.Count, cross.Comparisons.Count, EdgarReportPath);
        }
        // The audit: findings a person already reviewed stop counting; what is left is the worklist.
        var (audited, unused) = AuditLog.Apply(result.Checks, AuditLog.Read(paths.Resolve(AuditLog.DecisionsPath)));
        result = result with { Checks = audited.Select(a => a.Check).ToList() };
        AuditLog.WriteWorklist(paths.Resolve(AuditLog.WorklistPath), result.Checks);
        // Figures set by hand: listed in full, with any that no longer match a stored year or that the file has but the
        // database doesn't yet (--corrections copies them in).
        var corrections = SqliteDataStore.ReadCorrections(database);
        var stale = corrections.Where(c => !all.Financials.Any(f => f.PeriodType == CompanyPaisa.Contracts.PeriodType.Annual && f.FiscalYear == c.FiscalYear &&
                                                                   f.CompanyId.Equals(c.CompanyId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (!Publishing.CorrectionsFile.Read(paths.Resolve(Publishing.CorrectionsFile.Path)).SequenceEqual(corrections))
            logger.LogWarning("{File} differs from the database's corrections: run --corrections", Publishing.CorrectionsFile.Path);
        await File.WriteAllTextAsync(paths.Resolve(ReportPath), Render(result, audited, unused, corrections, stale, Path.GetFileName(database), markets), ct);

        foreach (var c in result.Checks.Where(c => c.Count > 0))
            logger.Log(c.Severity == Severity.Error ? LogLevel.Error : LogLevel.Warning, "{Area} · {Name}: {Count}", c.Area, c.Name, c.Count);
        logger.LogInformation("Validated {Companies} companies in {Markets} markets: {Errors} errors, {Warnings} open warnings, {Reviewed} reviewed → {Report}",
            all.Companies.Count, markets.Count, result.Errors, result.Warnings, audited.Sum(a => a.Reviewed.Count), ReportPath);
        if (stale.Count > 0) logger.LogWarning("{Stale} hand corrections match no stored year (listed in the report)", stale.Count);
        if (unused.Count > 0) logger.LogWarning("{Unused} audit decisions no longer match a finding (listed in the report)", unused.Count);
        return strict && result.Errors > 0 ? 1 : 0;
    }

    /// <summary>The website's own exchange rates, so "large" means the same thing here and on the site.</summary>
    private Dictionary<string, decimal> ReadRates()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(paths.Resolve(ApiSettings)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return doc.RootElement.GetProperty("Currency").GetProperty("UsdPer").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetDecimal(), StringComparer.OrdinalIgnoreCase);
    }

    private static string Render(ValidationResult r, IReadOnlyList<AuditedCheck> audited, IReadOnlyList<AuditDecision> unused,
        IReadOnlyList<Core.Services.FinancialCorrection> corrections, IReadOnlyList<Core.Services.FinancialCorrection> stale, string database, IReadOnlyList<string> markets)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Data validation report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Checks everything in `{database}` together, as the website sees it — markets: {string.Join(", ", markets)}.").AppendLine();
        sb.AppendLine("**Errors** are values that can't be right. **Warnings** are unusual values worth a look — many are real " +
                      "(big acquisitions, holding-company gains, mega stock grants). Nothing here changes the data.").AppendLine();
        var reviewedTotal = audited.Sum(a => a.Reviewed.Count);
        sb.AppendLine("Findings a person has already reviewed (`data/curated/audit-decisions.csv`) are not counted again; they are listed " +
                      "with their decision at the end. Every open finding is in `data/audit-worklist.csv`.").AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Errors: **{r.Errors}**").AppendLine(CultureInfo.InvariantCulture, $"- Warnings (open): **{r.Warnings}**")
            .AppendLine(CultureInfo.InvariantCulture, $"- Reviewed: **{reviewedTotal:N0}**").AppendLine();

        var reviewedBy = audited.ToDictionary(a => (a.Check.Area, a.Check.Name), a => a.Reviewed.Count);
        sb.AppendLine("## Checks").AppendLine().AppendLine("| Area | Check | Level | Open | Reviewed |").AppendLine("|---|---|---|---|---|");
        foreach (var c in r.Checks)
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {c.Area} | {c.Name} | {(c.Severity == Severity.Error ? "Error" : "Warning")} | {(c.Count == 0 ? "✓ 0" : c.Count.ToString("N0", CultureInfo.InvariantCulture))} | {(reviewedBy.GetValueOrDefault((c.Area, c.Name)) is var n && n > 0 ? n.ToString("N0", CultureInfo.InvariantCulture) : "")} |");
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
            if (c.Count > c.Examples.Count) sb.AppendLine(CultureInfo.InvariantCulture, $"- … and {c.Count - c.Examples.Count:N0} more (all in the worklist)");
            sb.AppendLine();
        }

        if (reviewedTotal > 0)
        {
            sb.AppendLine("## Reviewed").AppendLine().AppendLine("What a person decided about each finding, and why.").AppendLine();
            foreach (var a in audited.Where(a => a.Reviewed.Count > 0))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"### {a.Check.Area} · {a.Check.Name} ({a.Reviewed.Count:N0})").AppendLine();
                foreach (var (finding, d) in a.Reviewed)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"- {finding} — **{d.Decision}**: {d.Note}{(d.ReviewedOn is { } on ? $" ({on:yyyy-MM-dd})" : "")}");
                sb.AppendLine();
            }
        }
        if (unused.Count > 0)
        {
            sb.AppendLine("## Decisions that no longer match anything").AppendLine()
                .AppendLine("The finding has gone (the data changed). Remove the line from the decisions file, or mark it `fixed`.").AppendLine();
            foreach (var d in unused) sb.AppendLine(CultureInfo.InvariantCulture, $"- {d.Check} · {d.CompanyId}{(d.Match != "" ? $" · {d.Match}" : "")} — {d.Decision}: {d.Note}");
            sb.AppendLine();
        }
        if (corrections.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"## Figures set by hand ({corrections.Count})").AppendLine()
                .AppendLine("From `data/curated/financial-corrections.csv`; the site shows these instead of the imported figures.").AppendLine();
            foreach (var c in corrections)
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- {c.CompanyId} FY {c.FiscalYear}:{(c.Revenue is { } rv ? $" revenue {rv:N0}" : "")}{(c.NetIncome is { } ni ? $" net income {ni:N0}" : "")} — {c.Note}{(stale.Contains(c) ? " **(matches no stored year)**" : "")}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
