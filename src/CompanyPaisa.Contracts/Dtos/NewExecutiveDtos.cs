namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>
/// An officer appointment the company announced (8-K Item 5.02) with the package it stated: salary, sign-on cash, stock
/// awards… in <see cref="Currency"/>. What was announced, not pay received. <see cref="PersonId"/> is set when the person
/// already has a page (pay reported at this or another company).
/// </summary>
public sealed record NewExecutiveDto(
    string? PersonId,
    string Name,
    string Title,
    CompanyRefDto Company,
    DateOnly AnnouncedOn,
    DateOnly? StartsOn,
    decimal Total,
    string Currency,
    IReadOnlyList<PackageItemDto> Package,
    string? SourceFiling);

public sealed record PackageItemDto(PackageItemKind Kind, string Label, decimal Amount);
