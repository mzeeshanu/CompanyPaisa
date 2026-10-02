using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

public sealed record FinancialPeriod
{
    public required string CompanyId { get; init; }
    public required PeriodType PeriodType { get; init; }
    public required int FiscalYear { get; init; }
    /// <summary>1-4 for quarterly rows, null for annual rows.</summary>
    public int? FiscalQuarter { get; init; }
    public required decimal Revenue { get; init; }
    public required decimal NetIncome { get; init; }
    public decimal? OperatingIncome { get; init; }
    public decimal? Eps { get; init; }
    public string? SourceFiling { get; init; }

    /// <summary>Sortable key: quarterly 2025Q3 → 20253, annual 2025 → 20250.</summary>
    public int SortKey => FiscalYear * 10 + (FiscalQuarter ?? 0);

    public string Label => PeriodType == PeriodType.Quarterly ? $"Q{FiscalQuarter} {FiscalYear}" : $"FY {FiscalYear}";
}
