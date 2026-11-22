namespace CompanyPaisa.Core.Domain;

/// <summary>Describes the loaded data set (the "_meta" sheet).</summary>
/// <param name="AsOfDate">When any of the data was last refreshed: the newest of <paramref name="MarketDates"/>.</param>
/// <param name="MarketDates">When each market ("sec", "uk", "eu"…) was last refreshed.</param>
public sealed record DataSetMetadata(string DataVersion, DateOnly? AsOfDate, bool IsSampleData, DateTimeOffset LoadedAt,
    IReadOnlyDictionary<string, DateOnly>? MarketDates = null);
