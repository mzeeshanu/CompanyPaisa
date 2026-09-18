using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Features.Executives;

/// <summary>Officer appointments (with their announced packages) as the API shows them.</summary>
public static class NewExecutives
{
    /// <summary>An appointment counts as "new" in lists for this long after it starts (or was announced).</summary>
    public const int NewForMonths = 12;

    public static NewExecutiveDto ToDto(NewExecutive e, Company company, bool hasProfile) => new(
        hasProfile ? e.PersonId : null, e.Name, e.Title,
        new CompanyRefDto(company.Ticker, company.Name, company.Sector, company.PayCurrency ?? company.Currency),
        e.AnnouncedOn, e.StartsOn, e.Total, company.PayCurrency ?? company.Currency,
        e.Package.Select(p => new PackageItemDto(p.Kind, p.Label, p.Amount)).ToList(), e.SourceFiling);

    public static bool IsRecent(NewExecutive e, DateTimeOffset now) =>
        (e.StartsOn ?? e.AnnouncedOn) >= DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-NewForMonths);

    /// <summary>Which of these people have a page (reported pay somewhere), so their names can link to it.</summary>
    public static async Task<HashSet<string>> WithProfilesAsync(ICompanyRepository repository, IEnumerable<NewExecutive> rows, CancellationToken ct)
    {
        var ids = rows.Select(e => e.PersonId).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return new(StringComparer.OrdinalIgnoreCase);
        return (await repository.GetCompensationForPeopleAsync(ids, ct)).Select(p => p.PersonId).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
