using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// One year's figure set by hand after review (data/curated/financial-corrections.csv), for the few cases no import rule
/// can fix safely: Vivid Seats' total revenue, which its filings tag only as contract revenue in the SEC's figures; a
/// company whose real figure is small enough for <see cref="FinancialsCleanup"/> to take for a units slip (Tiptree's
/// $488 thousand of revenue after selling its insurer). A blank figure keeps the stored one; a corrected year is never
/// rescaled by the cleanup.
/// </summary>
/// <param name="Source">The filing the figure was read from (a URL), shown as the year's source.</param>
/// <param name="Note">What was checked: the statement line and why the import's figure was wrong.</param>
public sealed record FinancialCorrection(string CompanyId, int FiscalYear, decimal? Revenue, decimal? NetIncome, string Source, string Note);

public static class FinancialCorrections
{
    /// <summary>
    /// The annual rows with the corrections applied, the years they cover (for the cleanup to leave alone) and any correction
    /// that matched no stored year — a company gone or a year renumbered, to be reviewed rather than silently dropped.
    /// </summary>
    public static (List<FinancialPeriod> Financials, HashSet<(string CompanyId, int FiscalYear)> Covered, List<FinancialCorrection> Unmatched) Apply(
        IReadOnlyList<FinancialPeriod> financials, IReadOnlyList<FinancialCorrection> corrections)
    {
        var byYear = corrections.GroupBy(c => (Id: c.CompanyId.ToUpperInvariant(), c.FiscalYear)).ToDictionary(g => g.Key, g => g.Last());
        var matched = new HashSet<(string, int)>();
        var output = new List<FinancialPeriod>(financials.Count);
        foreach (var f in financials)
        {
            if (f.PeriodType != PeriodType.Annual || !byYear.TryGetValue((f.CompanyId.ToUpperInvariant(), f.FiscalYear), out var c))
            {
                output.Add(f);
                continue;
            }
            matched.Add((c.CompanyId.ToUpperInvariant(), c.FiscalYear));
            output.Add(f with
            {
                Revenue = c.Revenue ?? f.Revenue,
                NetIncome = c.NetIncome ?? f.NetIncome,
                SourceFiling = string.IsNullOrWhiteSpace(c.Source) ? f.SourceFiling : c.Source
            });
        }
        var covered = new HashSet<(string, int)>(matched, new KeyComparer());
        return (output, covered, byYear.Where(p => !matched.Contains(p.Key)).Select(p => p.Value).ToList());
    }

    private sealed class KeyComparer : IEqualityComparer<(string CompanyId, int FiscalYear)>
    {
        public bool Equals((string CompanyId, int FiscalYear) a, (string CompanyId, int FiscalYear) b) =>
            a.FiscalYear == b.FiscalYear && string.Equals(a.CompanyId, b.CompanyId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string CompanyId, int FiscalYear) k) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.CompanyId), k.FiscalYear);
    }
}
