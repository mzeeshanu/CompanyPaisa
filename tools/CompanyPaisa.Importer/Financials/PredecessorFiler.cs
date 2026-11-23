using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Financials;

/// <summary>
/// A company that reorganised under a new holding company files under a new SEC filer ID, and the SEC's company facts
/// start again from nothing (ExxonMobil Holdings in 2026: four quarters, no years). The old filer's history fills every
/// period the new one doesn't state. The new filer's own figures always win.
/// </summary>
internal static class PredecessorFiler
{
    internal const string Note = "Earlier periods from the predecessor filer";

    public static FinancialsResult? Merge(FinancialsResult? successor, FinancialsResult? predecessor, long predecessorCik, int years)
    {
        if (predecessor is not { Periods.Count: > 0 }) return successor;
        if (successor is not { Periods.Count: > 0 })
            return predecessor with { Notes = [.. predecessor.Notes, Describe(predecessorCik, predecessor.Periods)] };
        if (successor.Currency != predecessor.Currency) return successor;

        var have = successor.Periods.Select(Key).ToHashSet();
        var added = predecessor.Periods.Where(p => !have.Contains(Key(p))).ToList();
        if (added.Count == 0) return successor;
        var all = successor.Periods.Concat(added).ToList();
        var newestYear = all.Where(p => p.PeriodType == PeriodType.Annual).Select(p => (int?)p.FiscalYear).Max() ?? DateTime.UtcNow.Year;
        var from = newestYear - years + 1;
        var periods = all.Where(p => p.FiscalYear >= (p.PeriodType == PeriodType.Annual ? from : from - 1))
            .OrderBy(p => p.PeriodType).ThenBy(p => p.SortKey).ToList();
        added = added.Where(periods.Contains).ToList();
        return successor with
        {
            Periods = periods,
            Notes = [.. successor.Notes, Describe(predecessorCik, added)],
            LatestPeriodEnd = new[] { successor.LatestPeriodEnd, predecessor.LatestPeriodEnd }.Max(),
        };
    }

    private static (PeriodType, int, int?) Key(FinancialPeriod p) => (p.PeriodType, p.FiscalYear, p.FiscalQuarter);

    private static string Describe(long cik, IEnumerable<FinancialPeriod> periods)
    {
        var list = periods.ToList();
        var yearsAdded = list.Where(p => p.PeriodType == PeriodType.Annual).Select(p => p.FiscalYear).Order().ToList();
        var quarters = list.Count(p => p.PeriodType == PeriodType.Quarterly);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Note} (CIK {cik}): {(yearsAdded.Count > 0 ? $"years {yearsAdded[0]}–{yearsAdded[^1]}" : "no years")}, {quarters} quarter(s).");
    }
}
