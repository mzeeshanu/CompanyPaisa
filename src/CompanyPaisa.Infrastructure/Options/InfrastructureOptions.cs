using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Infrastructure.Options;

/// <summary>appsettings section "Caching".</summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;
    /// <summary>Cache profile name → seconds. Requests pick a profile (Search, Company, Reference).</summary>
    public Dictionary<string, int> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Cache capacity in rows: a search counts one per company or executive it returns, anything else counts one.</summary>
    [Range(10, 1_000_000)] public int MaxEntries { get; set; } = 10_000;
}

/// <summary>appsettings section "Pipeline".</summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>Requests slower than this are logged as warnings.</summary>
    [Range(1, 600_000)] public int SlowRequestThresholdMs { get; set; } = 500;
}
