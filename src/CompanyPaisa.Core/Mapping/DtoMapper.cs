using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Mapping;

/// <summary>Domain → contract conversions. Kept in one place so the public API shape is easy to review.</summary>
public static class DtoMapper
{
    public static GeoPointDto ToDto(this GeoPoint p) => new(p.Latitude, p.Longitude);

    public static LocationDto ToDto(this CompanyLocation l) =>
        new(l.LocationId, l.Type, l.Label, l.Street, l.City, l.State, l.PostalCode, l.Point.ToDto());

    public static GeoLookupDto ToDto(this GeoLookupResult g) => new(g.Query, g.City, g.State, g.PostalCode, g.Point.ToDto());

    public static CompanyIndicatorsDto ToDto(this CompanyIndicators i) => new(
        i.TtmRevenue,
        i.TtmNetIncome,
        i.RevenueGrowthYoY,
        i.RevenueCagr,
        i.CagrYears,
        i.NetMargin,
        i.Trend,
        i.LatestQuarter?.Label,
        i.LatestQuarter?.Revenue,
        i.LatestQuarter?.NetIncome,
        i.AnnualHistory.Select(y => new AnnualPointDto(y.FiscalYear, y.Revenue, y.NetIncome)).ToList());

    public static FinancialPeriodDto ToDto(this FinancialPeriod p, decimal? growth) =>
        new(p.Label, p.FiscalYear, p.FiscalQuarter, p.PeriodType, p.Revenue, p.NetIncome, growth, p.SourceFiling);
}
