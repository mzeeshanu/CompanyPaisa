using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Abstractions;

/// <summary>Distance maths. Default is straight-line (haversine).</summary>
public interface IDistanceCalculator
{
    double DistanceMiles(GeoPoint from, GeoPoint to);
    GeoBoundingBox BoundingBox(GeoPoint center, double radiusMiles);
}
