using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Reported revenue, profit and the other figures, by period.</summary>
public interface IFinancialsReader
{
    Task<IReadOnlyList<FinancialPeriod>> GetFinancialsAsync(string companyId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>>> GetFinancialsAsync(IEnumerable<string> companyIds, CancellationToken ct = default);
}
