namespace CompanyPaisa.Core.Domain;

/// <summary>Describes the loaded data set (the "_meta" sheet).</summary>
public sealed record DataSetMetadata(string DataVersion, DateOnly? AsOfDate, bool IsSampleData, DateTimeOffset LoadedAt);
