using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

/// <summary>Headline numbers computed from a company's financials.</summary>
public sealed record CompanyIndicators(
    decimal TtmRevenue,
    decimal TtmNetIncome,
    decimal? RevenueGrowthYoY,
    decimal? RevenueCagr,
    int CagrYears,
    decimal? NetMargin,
    TrendStatus Trend,
    FinancialPeriod? LatestQuarter,
    IReadOnlyList<FinancialPeriod> AnnualHistory)
{
    public static CompanyIndicators Empty(int cagrYears) =>
        new(0, 0, null, null, cagrYears, null, TrendStatus.Flat, null, []);
}
