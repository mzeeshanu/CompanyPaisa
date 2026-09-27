using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Named officers, what they were paid, and the appointments companies announced.</summary>
public interface IExecutivePayReader
{
    Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(string companyId, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutiveCompensation>> GetExecutiveCompensationAsync(IEnumerable<string> companyIds, CancellationToken ct = default);

    /// <summary>Every pay row for these people, at any company.</summary>
    Task<IReadOnlyList<ExecutiveCompensation>> GetCompensationForPeopleAsync(IEnumerable<string> personIds, CancellationToken ct = default);
    Task<Person?> GetPersonAsync(string personId, CancellationToken ct = default);

    /// <summary>Officer appointments these companies announced, with their stated packages.</summary>
    Task<IReadOnlyList<NewExecutive>> GetNewExecutivesAsync(IEnumerable<string> companyIds, CancellationToken ct = default);
}
