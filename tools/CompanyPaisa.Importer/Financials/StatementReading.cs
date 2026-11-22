namespace CompanyPaisa.Importer.Financials;

/// <summary>The top line of one year in an annual report's income statement, as EdgarTools read it.</summary>
/// <param name="End">The fiscal year's last day.</param>
/// <param name="Revenue">The revenue line.</param>
/// <param name="NetIncome">The same column's net income: shows the column is the period, currency and units we think.</param>
/// <param name="Label">The line's printed label.</param>
/// <param name="Concept">The line's XBRL concept, without its prefix.</param>
/// <param name="LargestTotal">The largest line the statement itself labels a total of revenue, if it has one.</param>
public sealed record StatementYear(DateOnly End, decimal? Revenue, decimal? NetIncome, string? Label, string? Concept, decimal? LargestTotal);

/// <summary>A company's newest annual report, read by tools/edgar/extract.py.</summary>
public sealed record StatementReading(string Accession, string Filed, IReadOnlyList<StatementYear> Years);
