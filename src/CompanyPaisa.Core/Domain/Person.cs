namespace CompanyPaisa.Core.Domain;

/// <summary>
/// A person who has been a named executive officer at one or more companies.
/// In real data <see cref="PersonId"/> should be the SEC reporting-owner CIK so one person is linked across filings.
/// </summary>
public sealed record Person
{
    public required string PersonId { get; init; }
    public required string Name { get; init; }
    public string? SecCik { get; init; }
}
