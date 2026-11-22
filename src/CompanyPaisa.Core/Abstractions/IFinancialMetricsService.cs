using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Computes TTM, growth, CAGR, margin and trend status.</summary>
public interface IFinancialMetricsService
{
    /// <param name="fiscalYearEnd">"MM-dd": tells whether the latest fiscal year is newer than the latest quarter.</param>
    CompanyIndicators Compute(IReadOnlyList<FinancialPeriod> periods, string? fiscalYearEnd = null);
    /// <summary>Adds year-over-year revenue growth to each period of one type.</summary>
    IReadOnlyList<(FinancialPeriod Period, decimal? RevenueGrowthYoY)> WithGrowth(IReadOnlyList<FinancialPeriod> periods, PeriodType type);
}
