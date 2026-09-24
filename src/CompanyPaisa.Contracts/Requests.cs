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
    /// <summary>
    /// A whole country or state instead of a radius: a code ("US", "US-TX", "CA-ON", "UK") or a name ("Texas"). A <see cref="Near"/>
    /// that names a region ("near=Texas") does the same. Distances are then 0 and <see cref="RadiusMiles"/> is ignored.
    /// </summary>
    public string? Region { get; init; }
    public string? Sector { get; init; }
    public bool HeadquarteredOnly { get; init; }
    public CompanySort? Sort { get; init; }
    /// <summary>The sort's opposite order (smallest first; farthest first for distance). Companies with no value stay at the end.</summary>
    public bool Reverse { get; init; }
    /// <summary>Narrows <c>Items</c> to names containing this text or tickers starting with it; the summary and bubbles still cover the whole area.</summary>
    public string? Search { get; init; }
    /// <summary>Also return every company in the area as a small <see cref="CompanyBubbleDto"/> (for drawing them all at once).</summary>
    public bool IncludeBubbles { get; init; }
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
    /// <summary>
    /// A whole country or state instead of a radius: a code ("US", "US-TX", "CA-ON", "UK") or a name ("Texas"). A <see cref="Near"/>
    /// that names a region ("near=Texas") does the same. Distances are then 0 and <see cref="RadiusMiles"/> is ignored.
    /// </summary>
    public string? Region { get; init; }
    public string? Sector { get; init; }
    /// <summary>Only executives of companies headquartered in the area (not those with just an office there).</summary>
    public bool HeadquarteredOnly { get; init; }
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
