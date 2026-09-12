namespace CompanyPaisa.Contracts;

// All money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>A point on the map.</summary>
public sealed record GeoPointDto(double Latitude, double Longitude);

/// <summary>Result of resolving a ZIP code or city to coordinates.</summary>
public sealed record GeoLookupDto(string Query, string City, string State, string? PostalCode, GeoPointDto Point);

/// <summary>A physical site of a company.</summary>
public sealed record LocationDto(
    string LocationId,
    LocationType Type,
    string Label,
    string Street,
    string City,
    string State,
    string PostalCode,
    GeoPointDto Point);

/// <summary>Headline numbers derived from a company's financials.</summary>
public sealed record CompanyIndicatorsDto(
    decimal TtmRevenue,
    decimal TtmNetIncome,
    decimal? RevenueGrowthYoY,
    decimal? RevenueCagr,
    int CagrYears,
    decimal? NetMargin,
    TrendStatus Trend,
    string? LatestQuarterLabel,
    decimal? LatestQuarterRevenue,
    decimal? LatestQuarterNetIncome,
    IReadOnlyList<AnnualPointDto> RevenueHistory);

/// <summary>One point of the annual revenue sparkline.</summary>
public sealed record AnnualPointDto(int FiscalYear, decimal Revenue, decimal NetIncome);

/// <summary>A company as it appears in search results.</summary>
public sealed record CompanySummaryDto(
    string Ticker,
    string Name,
    string Exchange,
    string Sector,
    bool IsHeadquarteredNearby,
    LocationDto NearestLocation,
    double DistanceMiles,
    CompanyIndicatorsDto Indicators);

/// <summary>Totals for the whole result set (not just the current page).</summary>
public sealed record NearbySummaryDto(int CompanyCount, decimal CombinedTtmRevenue, int GrowingCount, int HeadquarteredCount);

/// <summary>Response of the "companies near" search.</summary>
public sealed record NearbyCompaniesResponse(
    GeoPointDto Origin,
    string? OriginLabel,
    double RadiusMiles,
    CompanySort Sort,
    int Page,
    int PageSize,
    int TotalCount,
    NearbySummaryDto Summary,
    IReadOnlyList<CompanySummaryDto> Items);

/// <summary>Full company profile.</summary>
public sealed record CompanyDetailDto(
    string Ticker,
    string Name,
    string Exchange,
    string Sector,
    string? Industry,
    string? Website,
    int? Employees,
    decimal? MarketCap,
    string? Description,
    string Currency,
    string? FiscalYearEnd,
    DateOnly? AsOfDate,
    IReadOnlyList<LocationDto> Locations,
    CompanyIndicatorsDto Indicators);

/// <summary>One reporting period.</summary>
public sealed record FinancialPeriodDto(
    string Label,
    int FiscalYear,
    int? FiscalQuarter,
    PeriodType PeriodType,
    decimal Revenue,
    decimal NetIncome,
    decimal? RevenueGrowthYoY,
    string? SourceFiling);

public sealed record FinancialsResponse(string Ticker, PeriodType PeriodType, IReadOnlyList<FinancialPeriodDto> Periods);

/// <summary>One year of pay for one executive.</summary>
public sealed record ExecutiveCompYearDto(int Year, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, string? SourceFiling);

public sealed record ExecutiveDto(string ExecutiveId, string Name, string Title, IReadOnlyList<ExecutiveCompYearDto> History);

public sealed record ExecutivesResponse(string Ticker, IReadOnlyList<ExecutiveDto> Executives);

/// <summary>Information about the loaded data set.</summary>
public sealed record DataMetaDto(string DataVersion, DateOnly? AsOfDate, bool IsSampleData, int CompanyCount, int LocationCount, DateTimeOffset LoadedAt);

/// <summary>Settings the website needs, driven by appsettings.json.</summary>
public sealed record ClientConfigDto(
    string DefaultView,
    string DefaultTheme,
    double DefaultRadiusMiles,
    IReadOnlyList<double> AllowedRadiiMiles,
    CompanySort DefaultSort,
    bool ShowBaseMapByDefault,
    string? MapTilesUrl,
    string ConsentCookieName,
    int ConsentCookieDays,
    IReadOnlyDictionary<string, bool> Features);
