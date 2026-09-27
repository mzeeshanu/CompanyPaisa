using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Pay by job title, from filings and job ads rather than the company's own accounts.</summary>
public interface IJobSalaryReader
{
    /// <summary>Salaries by job title from the company's H-1B wage filings (company-wide rows and per-place rows).</summary>
    Task<IReadOnlyList<JobSalary>> GetJobSalariesAsync(string companyId, CancellationToken ct = default);

    /// <summary>Where each kind of job salaries comes from (visa filings, job ads); empty when there are none.</summary>
    Task<IReadOnlyList<JobSalarySource>> GetJobSalarySourcesAsync(CancellationToken ct = default);
}
