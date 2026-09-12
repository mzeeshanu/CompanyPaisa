using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>Straight-line ("as the crow flies") distance on a spherical Earth.</summary>
public sealed class HaversineDistanceCalculator : IDistanceCalculator
{
    private const double EarthRadiusMiles = 3958.8;
    private const double MilesPerDegreeLatitude = 69.0;

    public double DistanceMiles(GeoPoint from, GeoPoint to)
    {
        var dLat = ToRadians(to.Latitude - from.Latitude);
        var dLng = ToRadians(to.Longitude - from.Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusMiles * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public GeoBoundingBox BoundingBox(GeoPoint center, double radiusMiles)
    {
        var dLat = radiusMiles / MilesPerDegreeLatitude;
        // Longitude degrees shrink toward the poles; clamp to avoid dividing by ~0.
        var cosLat = Math.Max(0.01, Math.Cos(ToRadians(center.Latitude)));
        var dLng = radiusMiles / (MilesPerDegreeLatitude * cosLat);
        return new GeoBoundingBox(center.Latitude - dLat, center.Latitude + dLat, center.Longitude - dLng, center.Longitude + dLng);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
