using System.ComponentModel.DataAnnotations;

namespace CompanyPaisa.Data.Excel;

/// <summary>appsettings section "DataSource:Excel".</summary>
public sealed class ExcelDataSourceOptions
{
    public const string SectionName = "DataSource:Excel";

    /// <summary>Path to the .xlsx workbook. Relative paths resolve from the app's content root.</summary>
    [Required] public string Path { get; set; } = "";

    /// <summary>Reload automatically when the file is saved/replaced.</summary>
    public bool ReloadOnChange { get; set; } = true;

    /// <summary>Wait this long after the last file change before reloading (Excel writes files in several steps).</summary>
    [Range(0, 60_000)] public int ReloadDebounceMs { get; set; } = 1500;

    /// <summary>Sheet names, in case the workbook uses different ones.</summary>
    public ExcelSheetNames Sheets { get; set; } = new();
}

public sealed class ExcelSheetNames
{
    public string Companies { get; set; } = "Companies";
    public string Locations { get; set; } = "Locations";
    public string Financials { get; set; } = "Financials";
    public string ExecutiveCompensation { get; set; } = "ExecutiveCompensation";
    public string Meta { get; set; } = "_meta";
}
