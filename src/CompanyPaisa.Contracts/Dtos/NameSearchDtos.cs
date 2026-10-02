namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>
/// Companies and executives whose names match, best matches first (bigger companies and higher pay break ties), and the
/// place the text names, if any: a country, state or province (<see cref="NameSearchPlaceDto.Region"/> set) or a city.
/// </summary>
public sealed record NameSearchResponse(string Query, IReadOnlyList<NameSearchCompanyDto> Companies, IReadOnlyList<NameSearchExecutiveDto> Executives,
    IReadOnlyList<NameSearchPlaceDto>? Places = null);

/// <summary>A company found by name or ticker; <see cref="TtmRevenue"/> is in <see cref="Currency"/>.</summary>
public sealed record NameSearchCompanyDto(string Ticker, string Name, string Exchange, string Sector, string? City, string? State,
    decimal TtmRevenue, string Currency);

/// <summary>A person found by name, with the company and pay of their latest reported year (pay in the company's currency).</summary>
public sealed record NameSearchExecutiveDto(string PersonId, string Name, string Title, CompanyRefDto Company, int LatestYear, decimal LatestTotalPay);

/// <summary>A place the search text names: <see cref="Place"/> is how the website's search address names it ("utah", "Dallas, TX").</summary>
public sealed record NameSearchPlaceDto(string Label, string Place, RegionDto? Region);
