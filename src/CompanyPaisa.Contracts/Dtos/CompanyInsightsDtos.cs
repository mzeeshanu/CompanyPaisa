namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>
/// Facts worked out from the company's figures, each null when the data doesn't support it. Money is in <see cref="Currency"/>
/// unless the item carries its own.
/// </summary>
public sealed record CompanyInsightsResponse(
    string Ticker,
    string Currency,
    PayVsResultsDto? PayVsResults,
    RankDto? SectorRank,
    RankDto? CityRank,
    RevenueStreakDto? RevenueStreak,
    RecordsDto? Records,
    TopPaidCeoDto? CeoVsWorker,
    MarginComparisonDto? MarginVsSector,
    decimal? RevenuePerEmployee,
    int? Employees,
    decimal RevenuePerSecond,
    bool SimilarSameSector,
    IReadOnlyList<SimilarCompanyDto> Similar,
    // Officers appointed recently, newest first, with the package the company announced.
    IReadOnlyList<NewExecutiveDto>? NewExecutives = null,
    // How its executive pay compares with similar companies (same sector and size).
    PayVsPeersDto? PayVsPeers = null);

/// <summary>The chief executive's pay change against the company's revenue change for the same year.</summary>
public sealed record PayVsResultsDto(string PersonId, string Name, string Title, int Year, decimal TotalPay, string Currency,
    decimal PayChange, decimal RevenueChange);

/// <summary>Place by latest-12-month revenue among <see cref="Count"/> companies <see cref="Within"/> (a sector, or "Lehi, UT").</summary>
public sealed record RankDto(int Rank, int Count, string Within);

/// <summary>Consecutive periods, up to the latest, in which revenue grew (Up) or shrank (Down) against the year before.</summary>
public sealed record RevenueStreakDto(TrendStatus Direction, int Count, PeriodType Unit);

/// <summary>Best fiscal year for revenue in the history, and how many of those years were profitable.</summary>
public sealed record RecordsDto(int BestYear, decimal BestRevenue, bool BestIsLatest, int ProfitableYears, int YearsCounted);

/// <summary>Net margin (profit per unit of revenue) against the median of the sector's companies.</summary>
public sealed record MarginComparisonDto(decimal NetMargin, decimal SectorMedian, int SectorCount, string Sector);

/// <summary>A company near this one's headquarters (distance between headquarters).</summary>
public sealed record SimilarCompanyDto(string Ticker, string Name, string Sector, string City, string State, double DistanceMiles,
    decimal TtmRevenue, decimal? RevenueGrowthYoY, TrendStatus Trend, string Currency);

/// <summary>
/// How a company's executive pay compares with similar companies: same sector (and market: UK directors' pay is reported
/// differently), revenue between <see cref="MinRevenue"/> and <see cref="MaxRevenue"/>, pay reported for a recent year.
/// <see cref="TopRole"/> is "CEO" (or "top-paid executive director" in UK reports). Percentiles are the share of peers paid
/// less. <see cref="Nearby"/> is this company and the peers closest to it in size, highest pay first. Amounts in
/// <see cref="Currency"/> (converted at approximate rates when peers report in others: <see cref="Approximate"/>).
/// </summary>
public sealed record PayVsPeersDto(
    string Sector,
    decimal MinRevenue,
    decimal MaxRevenue,
    int PeerCount,
    string Currency,
    bool Approximate,
    string TopRole,
    decimal TopPay,
    decimal TopPayPeerMedian,
    int TopPayPercentile,
    decimal? OtherExecutivesPay,
    decimal? OtherExecutivesPeerMedian,
    int? OtherExecutivesPercentile,
    IReadOnlyList<PeerPayDto> Nearby);

/// <summary>One company's top executive pay in a peer comparison (in the comparing company's pay currency).</summary>
public sealed record PeerPayDto(string Ticker, string Name, string Role, decimal TopPay, int Year, bool IsThisCompany);
