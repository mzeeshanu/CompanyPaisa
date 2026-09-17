namespace CompanyPaisa.Contracts;

/// <summary>
/// Parameters for the "companies near" search. Give either a ZIP/city (<see cref="Near"/>)
/// or coordinates (<see cref="Latitude"/> + <see cref="Longitude"/>).
/// Anything left null falls back to the server's configured defaults.
/// </summary>
public sealed class NearbyCompaniesRequest
{
    /// <summary>ZIP code or "City, ST", e.g. "84043" or "Lehi, UT".</summary>
    public string? Near { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusMiles { get; init; }
    public string? Sector { get; init; }
    public bool HeadquarteredOnly { get; init; }
    public CompanySort? Sort { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

/// <summary>
/// Parameters for "executives near me": named executive officers of public companies that have a location
/// within the radius. Give either <see cref="Near"/> or coordinates.
/// </summary>
public sealed class ExecutivesNearRequest
{
    public string? Near { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? RadiusMiles { get; init; }
    public string? Sector { get; init; }
    /// <summary>Also include people who used to be executives at a nearby company but have since moved elsewhere.</summary>
    public bool IncludeFormer { get; init; }
    /// <summary>Case-insensitive match on name or title, e.g. "chief financial".</summary>
    public string? Search { get; init; }
    /// <summary>Only people whose title (current nearby role) holds this role, e.g. Ceo; "Former …" titles don't count.</summary>
    public ExecutiveRole? Role { get; init; }
    public ExecutiveSort? Sort { get; init; }
    /// <summary>History window for totals; defaults to Metrics:HistoryYears (10).</summary>
    public int? Years { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
