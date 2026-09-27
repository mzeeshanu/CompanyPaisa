namespace CompanyPaisa.Core.Domain;

public sealed record Company
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public required string Ticker { get; init; }
    public required string Exchange { get; init; }
    public required string Sector { get; init; }
    public string? Industry { get; init; }
    public string? Website { get; init; }
    /// <summary>The company's careers or jobs page, found by following the link on its website.</summary>
    public string? CareersUrl { get; init; }
    public int? Employees { get; init; }
    public decimal? MarketCap { get; init; }
    public string? Description { get; init; }
    public string Currency { get; init; } = "USD";
    /// <summary>Currency of executive pay when it differs from the accounts (many UK groups report in USD but pay in GBP).</summary>
    public string? PayCurrency { get; init; }
    public string? FiscalYearEnd { get; init; }
    public string? LogoUrl { get; init; }
    public DateOnly? AsOfDate { get; init; }
}
