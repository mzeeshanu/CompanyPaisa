using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

/// <summary>
/// Headline numbers computed from a company's financials. The totals cover the last 12 months, or the fiscal year
/// <paramref name="TotalsYear"/>; without <paramref name="HasTotals"/> neither could be worked out and they are zero.
/// </summary>
public sealed record CompanyIndicators(
    decimal TtmRevenue,
    decimal TtmNetIncome,
    decimal? RevenueGrowthYoY,
    decimal? RevenueCagr,
    int CagrYears,
    decimal? NetMargin,
    TrendStatus Trend,
    FinancialPeriod? LatestQuarter,
    IReadOnlyList<FinancialPeriod> AnnualHistory,
    int? TotalsYear = null,
    bool HasTotals = true)
{
    public static CompanyIndicators Empty(int cagrYears) =>
        new(0, 0, null, null, cagrYears, null, TrendStatus.Flat, null, [], HasTotals: false);
}
