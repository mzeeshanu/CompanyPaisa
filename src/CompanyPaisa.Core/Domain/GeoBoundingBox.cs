namespace CompanyPaisa.Core.Domain;

/// <summary>A lat/long rectangle, used as a cheap pre-filter before exact distance checks.</summary>
public readonly record struct GeoBoundingBox(double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)
{
    public bool Contains(GeoPoint p) =>
        p.Latitude >= MinLatitude && p.Latitude <= MaxLatitude &&
        p.Longitude >= MinLongitude && p.Longitude <= MaxLongitude;
}
