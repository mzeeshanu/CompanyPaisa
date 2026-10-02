using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

/// <summary>
/// An officer appointment a company announced, with the package it stated (read from the 8-K, not pay received).
/// <see cref="PersonId"/> links to the person's pay history when the importer could match them.
/// </summary>
public sealed record NewExecutive
{
    public required string CompanyId { get; init; }
    public string? PersonId { get; init; }
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required DateOnly AnnouncedOn { get; init; }
    public DateOnly? StartsOn { get; init; }
    public string? SourceFiling { get; init; }
    public required IReadOnlyList<PackageItem> Package { get; init; }

    public decimal Total => Package.Sum(p => p.Amount);
}

public sealed record PackageItem(PackageItemKind Kind, decimal Amount, string Label);
