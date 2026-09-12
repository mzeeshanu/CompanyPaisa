using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data.Excel;

/// <summary>An immutable, indexed copy of the whole workbook. Swapped atomically on reload.</summary>
internal sealed class DataSnapshot
{
    public DataSnapshot(
        IReadOnlyList<Company> companies,
        IReadOnlyList<CompanyLocation> locations,
        IReadOnlyList<FinancialPeriod> financials,
        IReadOnlyList<ExecutiveCompensation> executives,
        DataSetMetadata metadata)
    {
        Companies = companies;
        Locations = locations;
        Metadata = metadata;
        CompaniesById = companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        CompaniesByTicker = companies.GroupBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        LocationsByCompany = Group(locations, l => l.CompanyId);
        FinancialsByCompany = Group(financials, f => f.CompanyId);
        ExecutivesByCompany = Group(executives, e => e.CompanyId);
        Sectors = companies.Select(c => c.Sector).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<Company> Companies { get; }
    public IReadOnlyList<CompanyLocation> Locations { get; }
    public DataSetMetadata Metadata { get; }
    public IReadOnlyDictionary<string, Company> CompaniesById { get; }
    public IReadOnlyDictionary<string, Company> CompaniesByTicker { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<CompanyLocation>> LocationsByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>> FinancialsByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ExecutiveCompensation>> ExecutivesByCompany { get; }
    public IReadOnlyList<string> Sectors { get; }

    public Company? Find(string idOrTicker) =>
        CompaniesById.TryGetValue(idOrTicker, out var c) || CompaniesByTicker.TryGetValue(idOrTicker, out c) ? c : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<T>> Group<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.ToList(), StringComparer.OrdinalIgnoreCase);
}
