using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data;

/// <summary>An immutable, indexed copy of the whole data set. Swapped atomically on reload.</summary>
public sealed class DataSnapshot
{
    public DataSnapshot(CompanyData data)
    {
        Companies = data.Companies;
        Locations = data.Locations;
        Financials = data.Financials;
        Executives = data.Pay;
        People = data.People;
        NewExecutivesByCompany = Group(data.Appointments, e => e.CompanyId);
        WorkerPayByCompany = Group(data.WorkerPays, w => w.CompanyId);
        JobSalariesByCompany = Group(data.JobSalaries, j => j.CompanyId);
        SalarySources = data.SalarySources;
        Metadata = data.Metadata;
        CompaniesById = Companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        CompaniesByTicker = Companies.GroupBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        LocationsByCompany = Group(Locations, l => l.CompanyId);
        FinancialsByCompany = Group(Financials, f => f.CompanyId);
        ExecutivesByCompany = Group(Executives, e => e.CompanyId);
        CompensationByPerson = Group(Executives, e => e.PersonId);
        PeopleById = People.GroupBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Sectors = Companies.Select(c => c.Sector).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
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
    public IReadOnlyDictionary<string, IReadOnlyList<NewExecutive>> NewExecutivesByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<WorkerPay>> WorkerPayByCompany { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<JobSalary>> JobSalariesByCompany { get; }
    public IReadOnlyList<JobSalarySource> SalarySources { get; }
    public IReadOnlyList<string> Sectors { get; }

    public Company? Find(string idOrTicker) =>
        CompaniesById.TryGetValue(idOrTicker, out var c) || CompaniesByTicker.TryGetValue(idOrTicker, out c) ? c : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<T>> Group<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase)
             .ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.ToList(), StringComparer.OrdinalIgnoreCase);
}
