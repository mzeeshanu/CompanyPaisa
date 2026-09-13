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

/// <summary>How executives are ranked.</summary>
public enum ExecutiveSort
{
    /// <summary>Most recent year's total pay.</summary>
    Pay,
    /// <summary>Total pay over the history window (default 10 years), across all companies.</summary>
    TotalPay,
    /// <summary>Change in total pay versus the prior year.</summary>
    PayGrowth,
    Distance,
    Name
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
