namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>
/// Headline numbers derived from a company's financials. The Ttm figures are the last 12 months, or the fiscal year
/// <see cref="TotalsYear"/> when set; when <see cref="HasTotals"/> is false they are unknown (and zero).
/// </summary>
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
    IReadOnlyList<AnnualPointDto> RevenueHistory,
    int? TotalsYear = null,
    bool HasTotals = true);

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
    IReadOnlyList<CompanySummaryDto> Items,
    RegionDto? Region = null,
    IReadOnlyList<CompanyBubbleDto>? Bubbles = null);

/// <summary>
/// Just enough about one company to draw its bubble: every company in the area comes back this way (when asked for) while
/// <see cref="NearbyCompaniesResponse.Items"/> carries full details a page at a time. <see cref="TtmRevenue"/> is in <see cref="Currency"/>.
/// </summary>
public sealed record CompanyBubbleDto(
    string Ticker,
    string Name,
    decimal TtmRevenue,
    decimal? RevenueGrowthYoY,
    TrendStatus Trend,
    string City,
    string State,
    double DistanceMiles,
    bool IsHeadquarteredNearby,
    string Currency = "USD");

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
    string? PayCurrency = null,
    string? CareersUrl = null,
    IReadOnlyList<WorkerPayDto>? WorkerPay = null,
    int SalaryTitles = 0,
    string? PriceSymbol = null,
    IndustryClassificationDto? Classification = null);

/// <summary>
/// The category the company goes by, as its exchange (ICB, GICS, PSX sector) or the SEC (SIC code) publishes it.
/// </summary>
/// <param name="Scheme">"ICB", "GICS", "SIC" or "PSX".</param>
/// <param name="Code">The scheme's code, when known ("4510", "7372").</param>
/// <param name="Industry">The scheme's top level ("Consumer Staples").</param>
/// <param name="Name">The most detailed level published ("Food, Beverage and Tobacco").</param>
/// <param name="Source">Who publishes it ("Nasdaq Stockholm", "SEC EDGAR").</param>
/// <param name="CheckedOn">When it was last read from the source.</param>
public sealed record IndustryClassificationDto(string Scheme, string? Code, string? Industry, string Name, string Source, DateOnly? CheckedOn);

/// <summary>A company referred to from somewhere else (an executive's history, a search hit).</summary>
public sealed record CompanyRefDto(string Ticker, string Name, string Sector, string Currency = "USD");
