namespace CompanyPaisa.Analytics;

public sealed record AnalyticsReport(
    string From,
    string To,
    AnalyticsTotals Totals,
    IReadOnlyList<AnalyticsDay> Days,
    IReadOnlyList<AnalyticsCount> SearchPoints,
    IReadOnlyList<AnalyticsCount> Places,
    IReadOnlyList<AnalyticsCount> Companies,
    IReadOnlyList<AnalyticsCount> Executives,
    IReadOnlyList<AnalyticsCount> Countries,
    IReadOnlyList<AnalyticsCount> Cities,
    IReadOnlyList<AnalyticsCount> Devices,
    IReadOnlyList<AnalyticsCount> Browsers,
    IReadOnlyList<AnalyticsCount> OperatingSystems,
    IReadOnlyList<AnalyticsCount> Referrers,
    IReadOnlyList<AnalyticsCount> Actions,
    IReadOnlyList<AnalyticsCount> Sources);

/// <param name="Visitors">Distinct visitors per day, added up over the range (someone who came on two days counts twice).</param>
public sealed record AnalyticsTotals(int Visitors, int PageViews, int Searches, int CompanyViews, int ExecutiveViews, int Events);

public sealed record AnalyticsDay(string Day, int Visitors, int PageViews, int Searches, int CompanyViews, int ExecutiveViews);

/// <summary>A row of a "top" list: how often, and by how many visitors (per day, added up).</summary>
public sealed record AnalyticsCount(string Key, string? Label, int Count, int Visitors, double? Latitude = null, double? Longitude = null);
