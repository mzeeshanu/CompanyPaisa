namespace CompanyPaisa.Contracts;

/// <summary>Information about the loaded data set.</summary>
public sealed record DataMetaDto(string DataVersion, DateOnly? AsOfDate, bool IsSampleData, int CompanyCount, int LocationCount, DateTimeOffset LoadedAt);

/// <summary>Settings the website needs, driven by appsettings.json.</summary>
public sealed record ClientConfigDto(
    string DefaultTheme,
    double DefaultRadiusMiles,
    IReadOnlyList<double> AllowedRadiiMiles,
    CompanySort DefaultSort,
    string ConsentCookieName,
    int ConsentCookieDays,
    IReadOnlyDictionary<string, bool> Features,
    IReadOnlyList<CoverageAreaDto> Coverage,
    string? PrivacyContact = null);

/// <summary>An area the data set covers, with a ZIP / postcode district to try. Country: "US" or "UK".</summary>
public sealed record CoverageAreaDto(string Name, string ExampleZip, string Country = "US");
