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
    CompanyIndicatorsDto Indicators,
    string Currency = "USD");

/// <summary>Totals for the whole result set (not just the current page).</summary>
/// <remarks>
/// <see cref="CombinedTtmRevenue"/> is in <see cref="Currency"/> (the one most results use); <see cref="Approximate"/> means
/// some results were in other currencies and were converted at the configured approximate rates.
/// </remarks>
public sealed record NearbySummaryDto(int CompanyCount, decimal CombinedTtmRevenue, int GrowingCount, int HeadquarteredCount,
    string Currency = "USD", bool Approximate = false, TopPaidCeoDto? TopPaidCeo = null);

/// <summary>
/// The highest-paid chief executive of a company headquartered in the results (latest reported year), and how long they take
/// to earn a typical full-time worker's yearly pay in the company's home country. Null when no such pay is reported.
/// <see cref="Role"/> is "CEO", or "executive director" where the filing doesn't say who the chief executive is (UK annual reports).
/// </summary>
public sealed record TopPaidCeoDto(
    string PersonId,
    string Name,
    string Title,
    string Ticker,
    string CompanyName,
    int Year,
    decimal TotalPay,
    string Currency,
    string Role,
    MedianPayComparisonDto? MedianWorker);

/// <summary>
/// A country's median full-time pay and how many hours (of a 24/7 calendar year) the CEO takes to earn it.
/// <see cref="Approximate"/> = the CEO's pay was converted from another currency first.
/// </summary>
public sealed record MedianPayComparisonDto(
    string Country,
    string Description,
    decimal AnnualPay,
    string Currency,
    string Period,
    string Source,
    string SourceUrl,
    double HoursToEarn,
    bool Approximate);

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
    CompanyIndicatorsDto Indicators,
    string? PayCurrency = null);

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

/// <summary>An executive's pay at one company. <see cref="ExecutiveId"/> is the person id (same across companies).</summary>
public sealed record ExecutiveDto(string ExecutiveId, string Name, string Title, IReadOnlyList<ExecutiveCompYearDto> History);

// ---------- People / executives lookup ----------

public sealed record CompanyRefDto(string Ticker, string Name, string Sector, string Currency = "USD");

/// <summary>Total pay for one year; <see cref="Ticker"/> is the company that paid the most that year.</summary>
public sealed record PayPointDto(int Year, decimal Total, string Ticker);

/// <summary>An executive as they appear in the "executives near me" list.</summary>
public sealed record ExecutiveSummaryDto(
    string PersonId,
    string Name,
    string Title,
    CompanyRefDto Company,
    LocationDto NearestLocation,
    double DistanceMiles,
    bool IsCurrent,   // false = has since moved to a company outside the search area (only with includeFormer)
    int LatestYear,
    decimal LatestTotalPay,
    decimal? PayGrowthYoY,
    decimal WindowTotalPay,
    int WindowYears,
    int CompanyCount,
    IReadOnlyList<PayPointDto> PayHistory);

/// <remarks>Pay totals are in <see cref="Currency"/>; <see cref="Approximate"/> means some pay was converted from another currency.</remarks>
public sealed record ExecutivesNearSummaryDto(int ExecutiveCount, int CompanyCount, decimal CombinedLatestPay, decimal? MedianLatestPay, int? LatestYear,
    string Currency = "USD", bool Approximate = false);

public sealed record ExecutivesNearResponse(
    GeoPointDto Origin,
    string? OriginLabel,
    double RadiusMiles,
    ExecutiveSort Sort,
    int Page,
    int PageSize,
    int TotalCount,
    ExecutivesNearSummaryDto Summary,
    IReadOnlyList<ExecutiveSummaryDto> Items);

/// <summary>A stretch of a career at one company.</summary>
public sealed record ExecutiveRoleDto(CompanyRefDto Company, string Title, int FromYear, int ToYear, decimal TotalPay);

public sealed record ExecutivePayYearDto(int Year, CompanyRefDto Company, string Title, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, string? SourceFiling);

/// <summary>A person's full reported pay history, across every company they were a named executive at.</summary>
public sealed record ExecutiveDetailDto(
    string PersonId,
    string Name,
    string? SecCik,
    string CurrentTitle,
    CompanyRefDto CurrentCompany,
    int FirstYear,
    int LatestYear,
    decimal TotalPay,
    int YearsReported,
    IReadOnlyList<ExecutiveRoleDto> Roles,
    IReadOnlyList<ExecutivePayYearDto> History);

public sealed record ExecutivesResponse(string Ticker, IReadOnlyList<ExecutiveDto> Executives);

// ---------- Search by name ----------

/// <summary>A company found by name or ticker; <see cref="TtmRevenue"/> is in <see cref="Currency"/>.</summary>
public sealed record NameSearchCompanyDto(string Ticker, string Name, string Exchange, string Sector, string? City, string? State,
    decimal TtmRevenue, string Currency);

/// <summary>A person found by name, with the company and pay of their latest reported year (pay in the company's currency).</summary>
public sealed record NameSearchExecutiveDto(string PersonId, string Name, string Title, CompanyRefDto Company, int LatestYear, decimal LatestTotalPay);

/// <summary>Companies and executives whose names match, best matches first (bigger companies and higher pay break ties).</summary>
public sealed record NameSearchResponse(string Query, IReadOnlyList<NameSearchCompanyDto> Companies, IReadOnlyList<NameSearchExecutiveDto> Executives);

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
    IReadOnlyDictionary<string, bool> Features,
    IReadOnlyList<CoverageAreaDto> Coverage,
    string? PrivacyContact = null);

/// <summary>An area the data set covers, with a ZIP / postcode district to try. Country: "US" or "UK".</summary>
public sealed record CoverageAreaDto(string Name, string ExampleZip, string Country = "US");
