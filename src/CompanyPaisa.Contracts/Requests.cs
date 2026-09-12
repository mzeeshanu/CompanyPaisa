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
