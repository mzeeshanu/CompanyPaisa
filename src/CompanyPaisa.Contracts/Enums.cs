namespace CompanyPaisa.Contracts;

/// <summary>Overall direction of a company, used to tint its bubble.</summary>
public enum TrendStatus
{
    Up,
    Flat,
    Down
}

/// <summary>How results are ranked.</summary>
public enum CompanySort
{
    Revenue,
    Growth,
    Profit,
    Distance
}

/// <summary>Granularity of a financial period.</summary>
public enum PeriodType
{
    Quarterly,
    Annual
}

/// <summary>What kind of site a company location is.</summary>
public enum LocationType
{
    Headquarters,
    Campus,
    Office,
    Plant
}
