using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Companies themselves, and what the data set as a whole looks like.</summary>
public interface ICompanyReader
{
    Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default);
    Task<Company?> GetCompanyAsync(string companyIdOrTicker, CancellationToken ct = default);
    Task<IReadOnlyList<Company>> GetCompaniesAsync(IEnumerable<string> companyIds, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default);
    Task<DataSetMetadata> GetMetadataAsync(CancellationToken ct = default);
    Task<(int Companies, int Locations)> GetCountsAsync(CancellationToken ct = default);
}
