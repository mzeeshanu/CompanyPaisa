namespace CompanyPaisa.Contracts;

// Money values are in whole units of the company's reporting currency (USD for v1).

/// <summary>A point on the map.</summary>
public sealed record GeoPointDto(double Latitude, double Longitude);

/// <summary>
/// Result of resolving a ZIP code, postcode or city to coordinates, or a country / state / province name to a
/// <see cref="Region"/> (then <see cref="Point"/> is the middle of its companies and City is empty).
/// </summary>
public sealed record GeoLookupDto(string Query, string City, string State, string? PostalCode, GeoPointDto Point, RegionDto? Region = null);

/// <summary>
/// A whole country ("US", "UK") or a US state / Canadian province ("US-TX", "CA-ON"), searched as one area.
/// <see cref="Slug"/> is how the website's address names it ("texas", "united-kingdom"); <see cref="InSentence"/> is the name
/// as it reads after "in" ("the United States", "Texas").
/// </summary>
public sealed record RegionDto(string Code, string Name, RegionKind Kind, string Country, string Slug, string InSentence);

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
