namespace CompanyPaisa.Core.Domain;

/// <summary>A latitude/longitude pair in decimal degrees.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
