namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>What a company pays by job title, from each source there is (job ads first, then visa filings).</summary>
public sealed record JobSalariesResponse(string Ticker, string Currency, IReadOnlyList<JobSalarySetDto> Sources);

/// <summary>
/// One source's job salaries. <see cref="Kind"/>: "postings" (the pay ranges in the company's US job ads) or "h1b" (the
/// salaries it committed to in H-1B wage filings). <see cref="From"/>–<see cref="To"/>: the dates of those ads or filings.
/// </summary>
public sealed record JobSalarySetDto(string Kind, DateOnly From, DateOnly To, string Source, IReadOnlyList<JobSalaryDto> Titles);

/// <summary>
/// One job title: how many filings or ads, and the yearly salary. Visa filings: 25th percentile, median, 75th percentile,
/// lowest and highest offer. Job ads: the typical bottom, middle and top of the advertised range, the lowest bottom and
/// highest top; <see cref="Url"/> is a current ad.
/// </summary>
public sealed record JobSalaryDto(string Title, string? Occupation, int Filings, decimal Low, decimal Median, decimal High, decimal Min, decimal Max,
    IReadOnlyList<JobSalaryPlaceDto> Places, string? Url = null);

/// <summary>The same job title at one work place (only places with enough filings of their own).</summary>
public sealed record JobSalaryPlaceDto(string City, string State, int Filings, decimal Low, decimal Median, decimal High, double? Latitude, double? Longitude);
