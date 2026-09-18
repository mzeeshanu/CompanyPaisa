using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

/// <summary>A latitude/longitude pair in decimal degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

/// <summary>A lat/long rectangle, used as a cheap pre-filter before exact distance checks.</summary>
public readonly record struct GeoBoundingBox(double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)
{
    public bool Contains(GeoPoint p) =>
        p.Latitude >= MinLatitude && p.Latitude <= MaxLatitude &&
        p.Longitude >= MinLongitude && p.Longitude <= MaxLongitude;
}

public sealed record Company
{
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    public required string Ticker { get; init; }
    public required string Exchange { get; init; }
    public required string Sector { get; init; }
    public string? Industry { get; init; }
    public string? Website { get; init; }
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

public sealed record CompanyLocation
{
    public required string LocationId { get; init; }
    public required string CompanyId { get; init; }
    public required LocationType Type { get; init; }
    public required string Label { get; init; }
    public string Street { get; init; } = "";
    public required string City { get; init; }
    public required string State { get; init; }
    public string PostalCode { get; init; } = "";
    public required GeoPoint Point { get; init; }

    public bool IsHeadquarters => Type == LocationType.Headquarters;
}

public sealed record FinancialPeriod
{
    public required string CompanyId { get; init; }
    public required PeriodType PeriodType { get; init; }
    public required int FiscalYear { get; init; }
    /// <summary>1-4 for quarterly rows, null for annual rows.</summary>
    public int? FiscalQuarter { get; init; }
    public required decimal Revenue { get; init; }
    public required decimal NetIncome { get; init; }
    public decimal? OperatingIncome { get; init; }
    public decimal? Eps { get; init; }
    public string? SourceFiling { get; init; }

    /// <summary>Sortable key: quarterly 2025Q3 → 20253, annual 2025 → 20250.</summary>
    public int SortKey => FiscalYear * 10 + (FiscalQuarter ?? 0);

    public string Label => PeriodType == PeriodType.Quarterly ? $"Q{FiscalQuarter} {FiscalYear}" : $"FY {FiscalYear}";
}

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

/// <summary>Describes the loaded data set (the "_meta" sheet).</summary>
public sealed record DataSetMetadata(string DataVersion, DateOnly? AsOfDate, bool IsSampleData, DateTimeOffset LoadedAt);

/// <summary>A ZIP code or city resolved to a point.</summary>
public sealed record GeoLookupResult(string Query, string City, string State, string? PostalCode, GeoPoint Point);

/// <summary>Headline numbers computed from a company's financials.</summary>
public sealed record CompanyIndicators(
    decimal TtmRevenue,
    decimal TtmNetIncome,
    decimal? RevenueGrowthYoY,
    decimal? RevenueCagr,
    int CagrYears,
    decimal? NetMargin,
    TrendStatus Trend,
    FinancialPeriod? LatestQuarter,
    IReadOnlyList<FinancialPeriod> AnnualHistory)
{
    public static CompanyIndicators Empty(int cagrYears) =>
        new(0, 0, null, null, cagrYears, null, TrendStatus.Flat, null, []);
}
