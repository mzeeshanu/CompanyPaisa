namespace CompanyPaisa.Core.Domain;

/// <summary>One person's reported pay at one company for one fiscal year (DEF 14A summary compensation table).</summary>
public sealed record ExecutiveCompensation
{
    public required string CompanyId { get; init; }
    /// <summary>The person, shared across companies (not per company).</summary>
    public required string PersonId { get; init; }
    public required string ExecutiveName { get; init; }
    public required string Title { get; init; }
    public required int Year { get; init; }
    public decimal Salary { get; init; }
    public decimal Bonus { get; init; }
    public decimal StockAwards { get; init; }
    public decimal Other { get; init; }
    public decimal Total { get; init; }
    public string? SourceFiling { get; init; }
}
