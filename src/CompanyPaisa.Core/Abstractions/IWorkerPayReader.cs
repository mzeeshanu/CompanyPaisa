using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>What the rest of the workforce earns, as the company itself discloses it.</summary>
public interface IWorkerPayReader
{
    /// <summary>The company's disclosed median-employee pay and CEO pay ratio, by year.</summary>
    Task<IReadOnlyList<WorkerPay>> GetWorkerPayAsync(string companyId, CancellationToken ct = default);
}
