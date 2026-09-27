using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data;

/// <summary>An immutable, indexed copy of the whole data set. Swapped atomically on reload.</summary>
/// <remarks>
/// Only what is actually scanned whole is kept as a flat list (companies for the search indexes, locations for the map
/// box). Everything else is kept once, grouped by the key it is looked up by (<see cref="GroupedRows{T}"/>), so the
/// lists the data source built while reading can be collected instead of sitting in memory for the life of the process.
/// </remarks>
public sealed class DataSnapshot
{
    public DataSnapshot(CompanyData data)
    {
        Companies = data.Companies;
        Locations = data.Locations;
        SalarySources = data.SalarySources;
        Metadata = data.Metadata;

        CompaniesById = data.Companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        CompaniesByTicker = data.Companies.GroupBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        PeopleById = data.People.GroupBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        LocationsByCompany = GroupedRows<CompanyLocation>.Build(data.Locations, l => l.CompanyId);
        FinancialsByCompany = GroupedRows<FinancialPeriod>.Build(data.Financials, f => f.CompanyId);
        ExecutivesByCompany = GroupedRows<ExecutiveCompensation>.Build(data.Pay, e => e.CompanyId);
        CompensationByPerson = GroupedRows<ExecutiveCompensation>.Build(data.Pay, e => e.PersonId);
        NewExecutivesByCompany = GroupedRows<NewExecutive>.Build(data.Appointments, e => e.CompanyId);
        WorkerPayByCompany = GroupedRows<WorkerPay>.Build(data.WorkerPays, w => w.CompanyId);
        JobSalariesByCompany = GroupedRows<JobSalary>.Build(data.JobSalaries, j => j.CompanyId);

        Sectors = data.Companies.Select(c => c.Sector).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<Company> Companies { get; }
    public IReadOnlyList<CompanyLocation> Locations { get; }
    public DataSetMetadata Metadata { get; }
    public IReadOnlyDictionary<string, Company> CompaniesById { get; }
    public IReadOnlyDictionary<string, Company> CompaniesByTicker { get; }
    public IReadOnlyDictionary<string, Person> PeopleById { get; }
    public GroupedRows<CompanyLocation> LocationsByCompany { get; }
    public GroupedRows<FinancialPeriod> FinancialsByCompany { get; }
    public GroupedRows<ExecutiveCompensation> ExecutivesByCompany { get; }
    public GroupedRows<ExecutiveCompensation> CompensationByPerson { get; }
    public GroupedRows<NewExecutive> NewExecutivesByCompany { get; }
    public GroupedRows<WorkerPay> WorkerPayByCompany { get; }
    public GroupedRows<JobSalary> JobSalariesByCompany { get; }
    public IReadOnlyList<JobSalarySource> SalarySources { get; }
    public IReadOnlyList<string> Sectors { get; }

    public Company? Find(string idOrTicker) =>
        CompaniesById.TryGetValue(idOrTicker, out var c) || CompaniesByTicker.TryGetValue(idOrTicker, out c) ? c : null;
}
