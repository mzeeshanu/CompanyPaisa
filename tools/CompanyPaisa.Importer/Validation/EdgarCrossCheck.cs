using System.Globalization;
using System.Text;
using System.Text.Json;
using CompanyPaisa.Contracts;
using CompanyPaisa.Data;

namespace CompanyPaisa.Importer.Validation;

/// <summary>One year from a company's annual report as EdgarTools read it (tools/edgar/extract.py).</summary>
public sealed record EdgarPeriod(DateOnly End, decimal? Revenue, decimal? NetIncome, string? Label = null, string? Concept = null, decimal? LargestTotal = null);

/// <summary>What tools/edgar/extract.py wrote for one company: data/cache/edgar/&lt;company id&gt;.json.</summary>
public sealed record EdgarReport(string CompanyId, string Status, string? Form, string? Accession, string? Error, IReadOnlyList<EdgarPeriod> Periods,
    long Cik = 0, string? Filed = null)
{
    /// <summary>The same report as the SEC import uses it.</summary>
    public Financials.StatementReading? ToStatement() => Status != "ok" || Accession is null || Filed is null ? null
        : new(Accession, Filed, Periods.Select(p => new Financials.StatementYear(p.End, p.Revenue, p.NetIncome, p.Label, p.Concept, p.LargestTotal)).ToList());
}

/// <summary>One figure compared: ours against the annual report's own income statement.</summary>
public sealed record EdgarComparison(string CompanyId, int FiscalYear, string Measure, decimal Ours, decimal Theirs, string? Form)
{
    /// <summary>Difference as a share of the report's figure (1 = 100%).</summary>
    public decimal Gap => Theirs == 0 ? (Ours == 0 ? 0 : 1) : Math.Abs(Ours - Theirs) / Math.Abs(Theirs);
}

public sealed record EdgarCrossCheckResult(
    IReadOnlyList<EdgarReport> Reports,
    IReadOnlyList<EdgarComparison> Comparisons,
    IReadOnlyList<(string CompanyId, int FiscalYear)> OnlyInReport,
    IReadOnlyList<CheckResult> Checks);

/// <summary>
/// The audit of the SEC import (docs/edgartools-plan.md): compares what we published with each company's latest annual
/// report as EdgarTools reads it. The import already takes the report's top line where that can be proved right, so
/// what's listed here is what it couldn't prove either way. This check only reports.
/// </summary>
public static class EdgarCrossCheck
{
    /// <summary>Differences above this share of the report's figure are listed as warnings.</summary>
    public const decimal Threshold = 0.10m;
    /// <summary>Below this (US-dollar equivalent) a 10% difference is noise, not worth a look.</summary>
    private const decimal MinimumUsd = 10_000_000m;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Every company file in <paramref name="directory"/>; empty when extract.py hasn't been run.</summary>
    public static IReadOnlyList<EdgarReport> Load(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var reports = new List<EdgarReport>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var raw = JsonSerializer.Deserialize<RawReport>(File.ReadAllText(file), Json);
            if (raw?.CompanyId is null) continue;
            var periods = (raw.Periods ?? [])
                .Where(p => DateOnly.TryParseExact(p.End, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .Select(p => new EdgarPeriod(DateOnly.ParseExact(p.End!, "yyyy-MM-dd", CultureInfo.InvariantCulture), p.Revenue, p.NetIncome, p.Label, p.Concept, p.LargestTotal))
                .ToList();
            reports.Add(new EdgarReport(raw.CompanyId, raw.Status ?? "?", raw.Form, raw.Accession, raw.Error, periods, raw.Cik ?? 0, raw.Filed));
        }
        return reports;
    }

    /// <summary>Same rule as the SEC import: years ending in January or February belong to the previous fiscal year.</summary>
    public static int FiscalYearOf(DateOnly end) => end.Month <= 2 ? end.Year - 1 : end.Year;

    public static EdgarCrossCheckResult Compare(CompanyData data, IReadOnlyList<EdgarReport> reports, IReadOnlyDictionary<string, decimal> usdPer)
    {
        var companies = data.Companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var annual = data.Financials.Where(f => f.PeriodType == PeriodType.Annual)
            .GroupBy(f => (Id: f.CompanyId.ToUpperInvariant(), f.FiscalYear)).ToDictionary(g => g.Key, g => g.First());
        decimal Usd(string id, decimal v) => Math.Abs(v) * usdPer.GetValueOrDefault(companies.TryGetValue(id, out var c) ? c.Currency : "USD", 1m);
        string Name(string id) => companies.TryGetValue(id, out var c) ? $"{id} ({c.Name})" : id;

        var comparisons = new List<EdgarComparison>();
        var onlyInReport = new List<(string, int)>();
        foreach (var report in reports.Where(r => companies.ContainsKey(r.CompanyId)))
            foreach (var period in report.Periods)
            {
                var year = FiscalYearOf(period.End);
                if (!annual.TryGetValue((report.CompanyId.ToUpperInvariant(), year), out var ours))
                {
                    onlyInReport.Add((report.CompanyId, year));
                    continue;
                }
                if (period.Revenue is { } revenue) comparisons.Add(new(report.CompanyId, year, "Revenue", ours.Revenue, revenue, report.Form));
                if (period.NetIncome is { } netIncome) comparisons.Add(new(report.CompanyId, year, "Net income", ours.NetIncome, netIncome, report.Form));
            }

        IEnumerable<string> Differences(string measure) => comparisons
            .Where(c => c.Measure == measure && c.Gap > Threshold && Usd(c.CompanyId, Math.Max(Math.Abs(c.Ours), Math.Abs(c.Theirs))) >= MinimumUsd)
            .OrderByDescending(c => Usd(c.CompanyId, c.Ours - c.Theirs))
            .Select(c => $"{Name(c.CompanyId)} FY {c.FiscalYear}: ours {DataValidator.Money(c.Ours)}, {c.Form ?? "report"} {DataValidator.Money(c.Theirs)}");

        CheckResult Check(string name, string why, IEnumerable<string> failures)
        {
            var list = failures.ToList();
            return new CheckResult("EdgarTools", name, Severity.Warning, why, list.Count, list.Take(12).ToList(), list);
        }

        var checks = new List<CheckResult>
        {
            Check("Revenue differs from the annual report", "Our revenue is more than 10% away from the headline revenue line of the company's own 10-K/20-F/40-F income statement, as EdgarTools reads it. Either our concept choice or its mapping is wrong; data/edgar-crosscheck.md lists them all.",
                Differences("Revenue")),
            Check("Net income differs from the annual report", "Our net income is more than 10% away from the report's net income line. Often attributable-to-parent against consolidated (including minority interests); data/edgar-crosscheck.md lists them all.",
                Differences("Net income")),
        };
        return new EdgarCrossCheckResult(reports, comparisons, onlyInReport, checks);
    }

    /// <summary>data/edgar-crosscheck.md: the whole comparison, so a month of differences can be read in one place.</summary>
    public static string Render(EdgarCrossCheckResult r, CompanyData data, IReadOnlyDictionary<string, decimal> usdPer)
    {
        var companies = data.Companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        string Name(string id) => companies.TryGetValue(id, out var c) ? $"{id} ({c.Name})" : id;
        decimal Usd(string id, decimal v) => Math.Abs(v) * usdPer.GetValueOrDefault(companies.TryGetValue(id, out var c) ? c.Currency : "USD", 1m);
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.AppendLine(inv, $"# EdgarTools cross-check — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine("What the SEC import published (docs/edgartools-plan.md): revenue and net income against the headline lines of each " +
                      "company's latest annual report (10-K, 20-F or 40-F), read by [EdgarTools](https://github.com/dgunning/edgartools) " +
                      "(`tools/edgar/extract.py`). The import uses a report's top line only where it passes the checks; the rest are listed here. A report covers its latest three years.").AppendLine();

        var statuses = r.Reports.GroupBy(x => x.Status).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count():N0}");
        sb.AppendLine(inv, $"- Companies read: **{r.Reports.Count:N0}** ({string.Join(", ", statuses)})");
        sb.AppendLine(inv, $"- Years in a report but not in our data: **{r.OnlyInReport.Count:N0}**").AppendLine();

        sb.AppendLine("| Measure | Compared | Within 1% | 1–10% | Over 10% |").AppendLine("|---|---|---|---|---|");
        foreach (var measure in new[] { "Revenue", "Net income" })
        {
            var m = r.Comparisons.Where(c => c.Measure == measure).ToList();
            sb.AppendLine(inv, $"| {measure} | {m.Count:N0} | {m.Count(c => c.Gap <= 0.01m):N0} | {m.Count(c => c.Gap is > 0.01m and <= Threshold):N0} | {m.Count(c => c.Gap > Threshold):N0} |");
        }
        sb.AppendLine();

        foreach (var measure in new[] { "Revenue", "Net income" })
        {
            var over = r.Comparisons.Where(c => c.Measure == measure && c.Gap > Threshold).OrderByDescending(c => Usd(c.CompanyId, c.Ours - c.Theirs)).ToList();
            sb.AppendLine(inv, $"## {measure}: more than 10% apart ({over.Count:N0})").AppendLine();
            if (over.Count == 0) { sb.AppendLine("None.").AppendLine(); continue; }
            sb.AppendLine("| Company | FY | Ours | Annual report | Gap | Currency |").AppendLine("|---|---|---|---|---|---|");
            foreach (var c in over)
                sb.AppendLine(inv, $"| {Name(c.CompanyId)} | {c.FiscalYear} | {DataValidator.Money(c.Ours)} | {DataValidator.Money(c.Theirs)} ({c.Form}) | {(c.Gap * 100):0}% | {companies.GetValueOrDefault(c.CompanyId)?.Currency} |");
            sb.AppendLine();
        }

        var newer = r.OnlyInReport.GroupBy(x => x.CompanyId).Select(g => $"{Name(g.Key)}: FY {string.Join(", ", g.Select(x => x.FiscalYear).Order())}").ToList();
        sb.AppendLine(inv, $"## Years only the report has ({newer.Count:N0} companies)").AppendLine();
        sb.AppendLine("A year missing from our data: a report newer than our last import, or a year our consistency rules dropped.").AppendLine();
        foreach (var line in newer) sb.AppendLine($"- {line}");
        sb.AppendLine();

        var unread = r.Reports.Where(x => x.Status != "ok").OrderBy(x => x.Status, StringComparer.Ordinal).ThenBy(x => x.CompanyId, StringComparer.Ordinal).ToList();
        sb.AppendLine(inv, $"## Not read ({unread.Count:N0})").AppendLine();
        foreach (var x in unread) sb.AppendLine($"- {Name(x.CompanyId)}: {x.Status}{(x.Error is null ? "" : $" — {x.Error.ReplaceLineEndings(" ")}")}");
        return sb.ToString();
    }


    private sealed record RawReport(string? CompanyId, string? Status, string? Form, string? Accession, string? Error, List<RawPeriod>? Periods, long? Cik, string? Filed);
    private sealed record RawPeriod(string? End, decimal? Revenue, decimal? NetIncome, string? Label, string? Concept, decimal? LargestTotal);
}
