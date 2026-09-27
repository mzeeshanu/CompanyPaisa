namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>One year of pay for one executive.</summary>
public sealed record ExecutiveCompYearDto(int Year, decimal Salary, decimal Bonus, decimal StockAwards, decimal Other, decimal Total, string? SourceFiling);

/// <summary>An executive's pay at one company. <see cref="ExecutiveId"/> is the person id (same across companies).</summary>
public sealed record ExecutiveDto(string ExecutiveId, string Name, string Title, IReadOnlyList<ExecutiveCompYearDto> History);

public sealed record ExecutivesResponse(string Ticker, IReadOnlyList<ExecutiveDto> Executives);

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
    IReadOnlyList<PayPointDto> PayHistory,
    // Set when the person was appointed recently and their package was announced.
    NewExecutiveDto? NewHire = null,
    // False for someone known only from an appointment announcement: no pay history, so no person page yet.
    bool HasProfile = true);

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
    IReadOnlyList<ExecutiveSummaryDto> Items,
    RegionDto? Region = null);

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
