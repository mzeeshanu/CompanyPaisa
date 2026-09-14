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
        IReadOnlyList<Person> people,
        DataSetMetadata metadata)
    {
        Companies = companies;
        Locations = locations;
        Financials = financials;
        Executives = executives;
        People = people;
        Metadata = metadata;
        CompaniesById = companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        CompaniesByTicker = companies.GroupBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        LocationsByCompany = Group(locations, l => l.CompanyId);
        FinancialsByCompany = Group(financials, f => f.CompanyId);
        ExecutivesByCompany = Group(executives, e => e.CompanyId);
        CompensationByPerson = Group(executives, e => e.PersonId);
        PeopleById = people.GroupBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Sectors = companies.Select(c => c.Sector).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<Company> Companies { get; }
    public IReadOnlyList<CompanyLocation> Locations { get; }
    public IReadOnlyList<FinancialPeriod> Financials { get; }
    public IReadOnlyList<ExecutiveCompensation> Executives { get; }
    public IReadOnlyList<Person> People { get; }
    public DataSetMetadata Metadata { get; }
    public IReadOnlyDictionary<string, Company> CompaniesById { get; }
    public IReadOnlyDictionary<string, Company> CompaniesByTicker { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<CompanyLocation>> LocationsByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<FinancialPeriod>> FinancialsByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ExecutiveCompensation>> ExecutivesByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ExecutiveCompensation>> CompensationByPerson { get; }
    public IReadOnlyDictionary<string, Person> PeopleById { get; }
    public IReadOnlyList<string> Sectors { get; }

    /// <summary>
    /// One data set from several workbooks (e.g. US + UK). Each part is already validated; this only checks that
    /// ids don't collide across them. Metadata: versions joined, the oldest as-of date, sample if any part is.
    /// </summary>
    public static DataSnapshot Merge(IReadOnlyList<DataSnapshot> parts, string primaryPath)
    {
        var problems = new List<string>();
        foreach (var dup in parts.SelectMany(p => p.Companies).GroupBy(c => c.CompanyId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"company_id '{dup.Key}' appears in more than one workbook.");
        foreach (var dup in parts.SelectMany(p => p.Locations).GroupBy(l => l.LocationId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"location_id '{dup.Key}' appears in more than one workbook.");
        if (problems.Count > 0) throw new DataLoadException(primaryPath, problems);

        var meta = parts[0].Metadata with
        {
            DataVersion = string.Join('+', parts.Select(p => p.Metadata.DataVersion)),
            AsOfDate = parts.Select(p => p.Metadata.AsOfDate).Where(d => d is not null).Min(),
            IsSampleData = parts.Any(p => p.Metadata.IsSampleData)
        };
        return new DataSnapshot(
            parts.SelectMany(p => p.Companies).ToList(),
            parts.SelectMany(p => p.Locations).ToList(),
            parts.SelectMany(p => p.Financials).ToList(),
            parts.SelectMany(p => p.Executives).ToList(),
            // The same person id in two workbooks is the same person; keep the first record.
            parts.SelectMany(p => p.People).DistinctBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase).ToList(),
            meta);
    }

    public Company? Find(string idOrTicker) =>
        CompaniesById.TryGetValue(idOrTicker, out var c) || CompaniesByTicker.TryGetValue(idOrTicker, out c) ? c : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<T>> Group<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.ToList(), StringComparer.OrdinalIgnoreCase);
}
