using System.ComponentModel.DataAnnotations;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Options;

/// <summary>appsettings section "Search".</summary>
public sealed class SearchOptions : IValidatableObject
{
    public const string SectionName = "Search";

    public static readonly double[] DefaultAllowedRadii = [5, 10, 25, 50];

    [Range(0.1, 500)] public double DefaultRadiusMiles { get; set; } = 10;
    /// <summary>Radius choices shown in the UI. No initializer: the config binder appends to lists
    /// instead of replacing them, so defaults are applied in PostConfigure when the section is empty.</summary>
    public List<double> AllowedRadiiMiles { get; set; } = [];
    /// <summary>Largest radius an API caller may ask for (custom values allowed up to this).</summary>
    [Range(0.1, 1000)] public double MaxRadiusMiles { get; set; } = 100;
    public CompanySort DefaultSort { get; set; } = CompanySort.Revenue;
    [Range(1, 1000)] public int DefaultPageSize { get; set; } = 50;
    [Range(1, 5000)] public int MaxPageSize { get; set; } = 200;
    /// <summary>If the nearest company is farther than this, the search reports "outside coverage".</summary>
    [Range(1, 5000)] public double CoverageMiles { get; set; } = 60;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultRadiusMiles > MaxRadiusMiles)
            yield return new ValidationResult("DefaultRadiusMiles must be <= MaxRadiusMiles.", [nameof(DefaultRadiusMiles)]);
        if (DefaultPageSize > MaxPageSize)
            yield return new ValidationResult("DefaultPageSize must be <= MaxPageSize.", [nameof(DefaultPageSize)]);
        if (AllowedRadiiMiles.Any(r => r <= 0 || r > MaxRadiusMiles))
            yield return new ValidationResult("AllowedRadiiMiles must be between 0 and MaxRadiusMiles.", [nameof(AllowedRadiiMiles)]);
    }
}

/// <summary>appsettings section "Metrics" — thresholds behind the green/amber/red tint.</summary>
public sealed class MetricsOptions : IValidatableObject
{
    public const string SectionName = "Metrics";

    /// <summary>YoY revenue growth above this (percent) counts as growing.</summary>
    public decimal TrendUpThresholdPct { get; set; } = 4;
    /// <summary>YoY revenue growth below this (percent) counts as shrinking.</summary>
    public decimal TrendDownThresholdPct { get; set; } = -1;
    /// <summary>A company losing money over the last 12 months is always "down".</summary>
    public bool LossMakingIsDown { get; set; } = true;
    [Range(1, 30)] public int CagrYears { get; set; } = 5;
    [Range(1, 30)] public int HistoryYears { get; set; } = 10;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TrendDownThresholdPct >= TrendUpThresholdPct)
            yield return new ValidationResult("TrendDownThresholdPct must be lower than TrendUpThresholdPct.", [nameof(TrendDownThresholdPct)]);
    }
}

/// <summary>appsettings section "DataSource" — which storage implementation backs <c>ICompanyRepository</c>.</summary>
public sealed class DataSourceOptions
{
    public const string SectionName = "DataSource";

    /// <summary>"Sqlite" (the database the importers publish) or "Excel" (the sample or hand-edited workbooks).</summary>
    [Required] public string Provider { get; set; } = "Sqlite";
    /// <summary>Used by database providers. Keep real values in user-secrets / environment variables.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>appsettings section "Benchmarks" — outside figures the site compares company numbers with.</summary>
public sealed class BenchmarkOptions
{
    public const string SectionName = "Benchmarks";

    /// <summary>Median full-time pay by country code ("US", "UK", "CA", "AU" — the codes the coverage areas use).</summary>
    public Dictionary<string, MedianPayOptions> MedianPay { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One country's median full-time pay, from its statistics office.</summary>
public sealed class MedianPayOptions
{
    /// <summary>Who the figure describes, e.g. "US full-time worker".</summary>
    [Required] public string Description { get; set; } = "";
    [Range(1, 10_000_000)] public decimal AnnualPay { get; set; }
    [Required] public string Currency { get; set; } = "USD";
    /// <summary>When it was measured, e.g. "Q2 2026".</summary>
    [Required] public string Period { get; set; } = "";
    [Required] public string Source { get; set; } = "";
    [Required] public string SourceUrl { get; set; } = "";
}

/// <summary>appsettings section "Geo".</summary>
public sealed class GeoOptions
{
    public const string SectionName = "Geo";

    /// <summary>CSV with columns zip,city,state,latitude,longitude. Relative paths resolve from the app's content root.</summary>
    [Required] public string ZipTablePath { get; set; } = "";

    /// <summary>
    /// More tables in the same format, e.g. UK postcode districts (zip = "SW1A", state = "UK").
    /// A UK postcode typed in full ("SW1A 1AA") is looked up by its district. Missing files are skipped.
    /// </summary>
    public List<string> AdditionalTablePaths { get; set; } = [];
}
