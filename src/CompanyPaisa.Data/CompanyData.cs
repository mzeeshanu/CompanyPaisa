using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data;

/// <summary>The rows of a data set as the API sees them, whatever they were stored in (SQLite, Excel).</summary>
public sealed record CompanyData(
    IReadOnlyList<Company> Companies,
    IReadOnlyList<CompanyLocation> Locations,
    IReadOnlyList<FinancialPeriod> Financials,
    IReadOnlyList<ExecutiveCompensation> Pay,
    IReadOnlyList<Person> People,
    DataSetMetadata Metadata)
{
    /// <summary>
    /// Several parts (markets, workbooks) as one data set. The same person id in two parts is the same person (first record
    /// kept). Metadata: versions joined, the oldest as-of date, sample if any part is.
    /// </summary>
    public static CompanyData Combine(IReadOnlyList<CompanyData> parts)
    {
        if (parts.Count == 1) return parts[0];
        var meta = parts[0].Metadata with
        {
            DataVersion = string.Join('+', parts.Select(p => p.Metadata.DataVersion)),
            AsOfDate = parts.Select(p => p.Metadata.AsOfDate).Where(d => d is not null).Min(),
            IsSampleData = parts.Any(p => p.Metadata.IsSampleData)
        };
        return new CompanyData(
            parts.SelectMany(p => p.Companies).ToList(),
            parts.SelectMany(p => p.Locations).ToList(),
            parts.SelectMany(p => p.Financials).ToList(),
            parts.SelectMany(p => p.Pay).ToList(),
            parts.SelectMany(p => p.People).DistinctBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase).ToList(),
            meta);
    }
}

/// <summary>Thrown when a data set can't be loaded; lists every problem found.</summary>
public sealed class DataLoadException(string source, IReadOnlyList<string> problems)
    : Exception($"Couldn't load '{source}':{Environment.NewLine}  - " + string.Join(Environment.NewLine + "  - ", problems.Take(50)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Integrity rules every data source must pass before the API serves it: unique ids, no rows pointing at a missing
/// company, valid quarters and coordinates, one pay row per person, company and year.
/// </summary>
public static class DataRules
{
    public static IReadOnlyList<string> Problems(CompanyData d)
    {
        var problems = new List<string>();
        foreach (var dup in d.Companies.GroupBy(c => c.CompanyId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"Companies: company_id '{dup.Key}' appears {dup.Count()} times.");
        foreach (var dup in d.Locations.GroupBy(l => l.LocationId, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"Locations: location_id '{dup.Key}' appears {dup.Count()} times.");

        var ids = d.Companies.Select(c => c.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Orphans(string table, IEnumerable<string> refs)
        {
            foreach (var id in refs.Where(r => !ids.Contains(r)).Distinct(StringComparer.OrdinalIgnoreCase))
                problems.Add($"{table}: company_id '{id}' isn't a company.");
        }
        Orphans("Locations", d.Locations.Select(l => l.CompanyId));
        Orphans("Financials", d.Financials.Select(f => f.CompanyId));
        Orphans("ExecutiveCompensation", d.Pay.Select(e => e.CompanyId));

        foreach (var f in d.Financials.Where(f => f.PeriodType == PeriodType.Quarterly && f.FiscalQuarter is not (>= 1 and <= 4)))
            problems.Add($"Financials: {f.CompanyId} {f.FiscalYear} quarterly row needs fiscal_quarter 1-4.");
        foreach (var l in d.Locations.Where(l => !l.Point.IsValid))
            problems.Add($"Locations: {l.LocationId} has invalid coordinates.");
        foreach (var dup in d.Pay.GroupBy(e => (Person: e.PersonId.ToUpperInvariant(), Company: e.CompanyId.ToUpperInvariant(), e.Year)).Where(g => g.Count() > 1))
            problems.Add($"ExecutiveCompensation: person '{dup.Key.Person}' has {dup.Count()} rows for {dup.Key.Company} in {dup.Key.Year}.");
        return problems;
    }

    /// <summary>Throws <see cref="DataLoadException"/> naming <paramref name="source"/> if any rule fails.</summary>
    public static CompanyData Check(CompanyData d, string source)
    {
        var problems = Problems(d);
        return problems.Count > 0 ? throw new DataLoadException(source, problems) : d;
    }
}
