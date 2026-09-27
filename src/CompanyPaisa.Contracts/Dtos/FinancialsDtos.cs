namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>One reporting period.</summary>
public sealed record FinancialPeriodDto(
    string Label,
    int FiscalYear,
    int? FiscalQuarter,
    PeriodType PeriodType,
    decimal Revenue,
    decimal NetIncome,
    decimal? RevenueGrowthYoY,
    string? SourceFiling);

public sealed record FinancialsResponse(string Ticker, PeriodType PeriodType, IReadOnlyList<FinancialPeriodDto> Periods);
