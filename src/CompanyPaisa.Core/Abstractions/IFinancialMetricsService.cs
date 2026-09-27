using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Computes TTM, growth, CAGR, margin and trend status.</summary>
public interface IFinancialMetricsService
{
    CompanyIndicators Compute(IReadOnlyList<FinancialPeriod> periods);
    /// <summary>Adds year-over-year revenue growth to each period of one type.</summary>
    IReadOnlyList<(FinancialPeriod Period, decimal? RevenueGrowthYoY)> WithGrowth(IReadOnlyList<FinancialPeriod> periods, PeriodType type);
}
