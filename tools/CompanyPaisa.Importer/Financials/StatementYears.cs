using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Financials;

/// <summary>
/// The year the SEC's company facts don't have yet but the newest annual report prints (BHP and Clorox 2026, L3Harris
/// and CGI 2025). Added only when the same statement's column for our newest year agrees on revenue and net income,
/// which shows it is the same line, currency and units. One year at a time, and never for banks.
/// </summary>
internal static class StatementYears
{
    internal const string AddedNote = "Newest year taken from the annual report";

    public static FinancialsResult AddNewest(FinancialsResult result, long cik, int years, StatementReading? statement)
    {
        if (statement is null || result.Notes.Any(n => n.EndsWith("(bank).", StringComparison.Ordinal))) return result;
        if (result.Periods.Where(p => p.PeriodType == PeriodType.Annual).MaxBy(p => p.FiscalYear) is not { } ours) return result;
        if (ours.Revenue <= 0 || ours.NetIncome == 0) return result;

        var columns = statement.Years.Where(y => y.Revenue is not null)
            .ToLookup(y => XbrlFinancialsExtractor.FiscalYearLabel(y.End));
        var newer = columns.Where(g => g.Key > ours.FiscalYear).SelectMany(g => g).ToList();
        var same = columns[ours.FiscalYear].ToList();
        if (newer.Count != 1 || same.Count != 1) return result;

        var (next, prior) = (newer[0], same[0]);
        if (XbrlFinancialsExtractor.FiscalYearLabel(next.End) != ours.FiscalYear + 1) return result;
        if (next.Revenue is not { } revenue || revenue <= 0 || next.NetIncome is not { } profit || profit == 0) return result;
        if (next.Label is not { } label || label != prior.Label || !XbrlFinancialsExtractor.IsHeadline(label)) return result;
        if (next.LargestTotal is { } total && total > revenue * 1.01m) return result;
        if (prior.NetIncome is not { } priorProfit || !Agrees(ours.Revenue, prior.Revenue!.Value) || !Agrees(ours.NetIncome, priorProfit)) return result;

        var added = ours with
        {
            FiscalYear = ours.FiscalYear + 1, Revenue = revenue, NetIncome = profit, OperatingIncome = null, Eps = null,
            SourceFiling = XbrlFinancialsExtractor.FilingUrl(cik, statement.Accession),
        };
        return result with
        {
            Periods = result.Periods.Where(p => p.PeriodType != PeriodType.Annual || p.FiscalYear > added.FiscalYear - years)
                .Append(added).OrderBy(p => p.PeriodType).ThenBy(p => p.SortKey).ToList(),
            Notes = [.. result.Notes, string.Create(CultureInfo.InvariantCulture, $"{AddedNote}: {next.End:yyyy-MM-dd} {revenue:N0}.")],
            LatestPeriodEnd = result.LatestPeriodEnd is { } had && had > next.End ? had : next.End,
        };
    }

    private static bool Agrees(decimal ours, decimal stated) => Math.Abs(ours - stated) <= 0.01m * Math.Abs(stated);
}
