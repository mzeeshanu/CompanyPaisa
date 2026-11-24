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
    /// <summary>The importer run the company came from ("sec", "uk", "eu"…), where the data source records it.</summary>
    public string? Market { get; init; }
    /// <summary>The industry its exchange (or, for US filers, the SEC) classifies it under, as published there.</summary>
    public IndustryClassification? Classification { get; init; }
}

/// <summary>
/// The category a company actually goes by: the classification its exchange publishes for the listing (ICB in London,
/// Euronext and Nasdaq Nordic/Baltic; GICS on the ASX; the PSX sector) or the SEC's industry code for US filers.
/// </summary>
public sealed record IndustryClassification
{
    /// <summary>"ICB", "GICS", "SIC" or "PSX".</summary>
    public required string Scheme { get; init; }
    /// <summary>The scheme's code where published or known (ICB super-sector "4510", SIC "7372").</summary>
    public string? Code { get; init; }
    /// <summary>The scheme's top level, e.g. ICB industry "Consumer Staples".</summary>
    public string? Industry { get; init; }
    /// <summary>The most detailed level published, e.g. ICB super-sector "Food, Beverage and Tobacco".</summary>
    public required string Name { get; init; }
    /// <summary>Who publishes it: "Nasdaq Stockholm", "London Stock Exchange", "SEC EDGAR"…</summary>
    public required string Source { get; init; }
    /// <summary>When it was last read from the source.</summary>
    public DateOnly? CheckedOn { get; init; }
}
