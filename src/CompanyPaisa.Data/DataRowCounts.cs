namespace CompanyPaisa.Data;

/// <summary>
/// How many rows of each kind the loaded data set holds, for the memory report (/health/memory). Reports nothing while
/// the data hasn't been loaded yet, so asking never triggers a load.
/// </summary>
public interface IDataRowCounts
{
    /// <summary>Row counts by kind, or an empty list while no data set is loaded.</summary>
    IReadOnlyList<DataRowCount> RowCounts();
}

/// <summary>One kind of row and how many of them are in memory.</summary>
public sealed record DataRowCount(string Kind, int Rows);
