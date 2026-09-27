namespace CompanyPaisa.Core.Abstractions;

/// <summary>
/// Raised by a data source when its data changes (e.g. the workbook was replaced).
/// Caches include <see cref="Generation"/> in their keys, so a change invalidates everything at once.
/// </summary>
public interface IDataChangeSignal
{
    long Generation { get; }
    void Signal();
}
