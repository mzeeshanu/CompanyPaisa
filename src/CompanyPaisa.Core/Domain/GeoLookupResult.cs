namespace CompanyPaisa.Core.Domain;

/// <summary>A ZIP code or city resolved to a point.</summary>
public sealed record GeoLookupResult(string Query, string City, string State, string? PostalCode, GeoPoint Point);
