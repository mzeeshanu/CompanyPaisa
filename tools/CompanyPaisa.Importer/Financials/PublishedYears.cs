using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Financials;

/// <summary>
/// Stops a company going backwards when the SEC's company facts lose years they used to hold (Brookfield Business Corp
/// lost 2023–25 after its 2026 reorganisation). A published year newer than everything extracted is kept only while
/// the company's own newest annual report states the same revenue and net income for it.
/// </summary>
internal static class PublishedYears
{
    internal const string KeptNote = "Published year kept, confirmed by the annual report";

    /// <param name="published">What the database being replaced holds under this company's id.</param>
    public static FinancialsResult Keep(FinancialsResult extracted, long cik, int years,
        IReadOnlyList<FinancialPeriod> published, string? publishedCurrency, StatementReading? statement)
    {
        if (statement is null || publishedCurrency != extracted.Currency) return extracted;
        var newest = extracted.Periods.Where(p => p.PeriodType == PeriodType.Annual).Select(p => (int?)p.FiscalYear).Max() ?? 0;
        // The same filer, not an earlier company that had the ticker.
        var filer = string.Create(CultureInfo.InvariantCulture, $"/edgar/data/{cik}/");

        var kept = new List<(FinancialPeriod Period, DateOnly End)>();
        foreach (var period in published.Where(p => p.PeriodType == PeriodType.Annual && p.FiscalYear > newest))
        {
            if (period.SourceFiling?.Contains(filer, StringComparison.Ordinal) != true) continue;
            var stated = statement.Years.FirstOrDefault(y => XbrlFinancialsExtractor.FiscalYearLabel(y.End) == period.FiscalYear);
            if (stated is { Revenue: { } revenue, NetIncome: { } profit } && Agrees(period.Revenue, revenue) && Agrees(period.NetIncome, profit))
                kept.Add((period, stated.End));
        }
        if (kept.Count == 0) return extracted;

        var from = kept.Max(k => k.Period.FiscalYear) - years + 1;
        var periods = extracted.Periods.Concat(kept.Select(k => k.Period))
            .Where(p => p.PeriodType != PeriodType.Annual || p.FiscalYear >= from)
            .OrderBy(p => p.PeriodType).ThenBy(p => p.SortKey).ToList();
        var end = kept.Max(k => k.End);
        return extracted with
        {
            Periods = periods,
            Notes = [.. extracted.Notes, $"{KeptNote}: {string.Join(", ", kept.Select(k => k.Period.FiscalYear).Order())}."],
            LatestPeriodEnd = extracted.LatestPeriodEnd is { } had && had > end ? had : end,
        };
    }

    private static bool Agrees(decimal ours, decimal stated) => Math.Abs(ours - stated) <= 0.01m * Math.Abs(stated);
}
