using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Domain;

public sealed record CompanyLocation
{
    public required string LocationId { get; init; }
    public required string CompanyId { get; init; }
    public required LocationType Type { get; init; }
    public required string Label { get; init; }
    public string Street { get; init; } = "";
    public required string City { get; init; }
    public required string State { get; init; }
    public string PostalCode { get; init; } = "";
    public required GeoPoint Point { get; init; }

    public bool IsHeadquarters => Type == LocationType.Headquarters;
}
