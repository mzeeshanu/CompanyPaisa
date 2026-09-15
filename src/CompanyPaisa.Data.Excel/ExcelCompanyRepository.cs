using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Data.Excel;

/// <summary>
/// The data set from Excel workbooks (the sample data, or a hand-edited set). The main workbook plus any additional
/// ones are read as one data set; saving any of them reloads it.
/// </summary>
public sealed class ExcelCompanyRepository(
    IOptions<ExcelDataSourceOptions> options,
    IFilePathResolver paths,
    IClock clock,
    IDataChangeSignal changeSignal,
    ILogger<ExcelCompanyRepository> logger)
    : SnapshotRepository(Files(options.Value, paths), options.Value.ReloadOnChange, options.Value.ReloadDebounceMs, clock, changeSignal, logger)
{
    private readonly ExcelDataSourceOptions _options = options.Value;
    private readonly IReadOnlyList<string> _files = Files(options.Value, paths);

    protected override string Source => _files[0];

    protected override CompanyData Read(DateTimeOffset loadedAt)
    {
        var parts = new List<CompanyData> { ExcelWorkbookReader.Read(_files[0], _options.Sheets, loadedAt) };
        foreach (var extra in _files.Skip(1))
        {
            if (!File.Exists(extra)) { logger.LogWarning("Additional workbook {Path} not found; skipping it", extra); continue; }
            parts.Add(ExcelWorkbookReader.Read(extra, _options.Sheets, loadedAt));
        }
        return CompanyData.Combine(parts);
    }

    private static IReadOnlyList<string> Files(ExcelDataSourceOptions o, IFilePathResolver paths) =>
        o.AdditionalPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(paths.Resolve).Prepend(paths.Resolve(o.Path)).ToList();
}
