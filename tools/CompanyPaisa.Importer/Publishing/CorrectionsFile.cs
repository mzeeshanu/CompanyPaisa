using System.Globalization;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Importer.Enrichment;

namespace CompanyPaisa.Importer.Publishing;

/// <summary>
/// data/curated/financial-corrections.csv: figures set by hand after review (company_id, fiscal_year, revenue, net_income,
/// source, note, reviewed_on; a blank figure keeps the imported one). Copied into the database's own table by
/// --corrections and after every import, so the site applies them as it loads (<see cref="FinancialCorrections"/>).
/// </summary>
public static class CorrectionsFile
{
    public const string Path = "data/curated/financial-corrections.csv";

    public static List<FinancialCorrection> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var list = new List<FinancialCorrection>();
        foreach (var f in Csv.Read(path).Skip(1))
        {
            if (f.Count < 6 || f[0].Trim() == "") continue;
            if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                throw new FormatException($"{path}: '{f[1]}' is not a fiscal year ({f[0]})");
            var revenue = Amount(f[2], path, f[0]);
            var income = Amount(f[3], path, f[0]);
            if (revenue is null && income is null) throw new FormatException($"{path}: {f[0]} {year} sets no figure");
            if (f[4].Trim() == "" || f[5].Trim() == "") throw new FormatException($"{path}: {f[0]} {year} needs a source and a note");
            list.Add(new FinancialCorrection(f[0].Trim().ToUpperInvariant(), year, revenue, income, f[4].Trim(), f[5].Trim()));
        }
        return list;
    }

    private static decimal? Amount(string text, string path, string company) =>
        text.Trim() == "" ? null
        : decimal.TryParse(text.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v
        : throw new FormatException($"{path}: '{text}' is not an amount ({company})");

    /// <summary>Copies the file into the database; returns the corrections and those that match no stored year.</summary>
    public static (int Applied, List<FinancialCorrection> Unmatched) Publish(string csvPath, string databasePath)
    {
        var corrections = Read(csvPath);
        SqliteDataStore.ReplaceCorrections(databasePath, corrections);
        var data = SqliteDataStore.Read(databasePath, DateTimeOffset.UtcNow);
        var unmatched = corrections.Where(c => !data.Financials.Any(f => f.PeriodType == CompanyPaisa.Contracts.PeriodType.Annual && f.FiscalYear == c.FiscalYear &&
                                                                         f.CompanyId.Equals(c.CompanyId, StringComparison.OrdinalIgnoreCase))).ToList();
        return (corrections.Count - unmatched.Count, unmatched);
    }
}
