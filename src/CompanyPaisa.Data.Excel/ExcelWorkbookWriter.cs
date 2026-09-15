using ClosedXML.Excel;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data.Excel;

/// <summary>Writes a workbook in the exact layout <see cref="ExcelCompanyRepository"/> reads. Used by tools (sample data, importers).</summary>
public static class ExcelWorkbookWriter
{
    /// <summary>
    /// Reads a workbook back with the same loader and validation the API uses; throws <see cref="DataLoadException"/>
    /// listing every problem. Tools call this after writing so a bad workbook never reaches the website.
    /// </summary>
    public static (int Companies, int Locations) Verify(string path)
    {
        var snapshot = ExcelWorkbookReader.Read(path, new ExcelSheetNames(), DateTimeOffset.UtcNow);
        return (snapshot.Companies.Count, snapshot.Locations.Count);
    }

    /// <summary>Everything in a workbook, read and validated exactly as the API loads it (for data-quality checks).</summary>
    public static WorkbookContents Read(string path)
    {
        var s = ExcelWorkbookReader.Read(path, new ExcelSheetNames(), DateTimeOffset.UtcNow);
        return new WorkbookContents(s.Companies, s.Locations, s.Financials, s.Executives, s.People, s.Metadata);
    }

    public static void Write(
        string path,
        IEnumerable<Company> companies,
        IEnumerable<CompanyLocation> locations,
        IEnumerable<FinancialPeriod> financials,
        IEnumerable<ExecutiveCompensation> executives,
        IEnumerable<Person> people,
        IReadOnlyDictionary<string, string> meta,
        ExcelSheetNames? sheetNames = null)
    {
        var s = sheetNames ?? new ExcelSheetNames();
        using var wb = new XLWorkbook();

        AddSheet(wb, s.Companies,
            ["company_id", "name", "ticker", "exchange", "sector", "industry", "website", "employees", "market_cap", "description", "currency", "pay_currency", "fiscal_year_end", "logo_url", "as_of_date"],
            companies.Select(c => new object?[] { c.CompanyId, c.Name, c.Ticker, c.Exchange, c.Sector, c.Industry, c.Website, c.Employees, c.MarketCap, c.Description, c.Currency, c.PayCurrency, c.FiscalYearEnd, c.LogoUrl, c.AsOfDate?.ToString("yyyy-MM-dd") }));

        AddSheet(wb, s.Locations,
            ["location_id", "company_id", "type", "label", "street", "city", "state", "postal_code", "latitude", "longitude"],
            locations.Select(l => new object?[] { l.LocationId, l.CompanyId, l.Type.ToString(), l.Label, l.Street, l.City, l.State, l.PostalCode, l.Point.Latitude, l.Point.Longitude }));

        AddSheet(wb, s.Financials,
            ["company_id", "period_type", "fiscal_year", "fiscal_quarter", "revenue", "net_income", "operating_income", "eps", "source_filing"],
            financials.Select(f => new object?[] { f.CompanyId, f.PeriodType.ToString(), f.FiscalYear, f.FiscalQuarter, f.Revenue, f.NetIncome, f.OperatingIncome, f.Eps, f.SourceFiling }));

        AddSheet(wb, s.ExecutiveCompensation,
            ["company_id", "person_id", "exec_name", "title", "year", "salary", "bonus", "stock_awards", "other", "total", "source_filing"],
            executives.Select(e => new object?[] { e.CompanyId, e.PersonId, e.ExecutiveName, e.Title, e.Year, e.Salary, e.Bonus, e.StockAwards, e.Other, e.Total, e.SourceFiling }));

        AddSheet(wb, s.People, ["person_id", "name", "sec_cik"], people.Select(p => new object?[] { p.PersonId, p.Name, p.SecCik }));

        AddSheet(wb, s.Meta, ["key", "value"], meta.Select(kv => new object?[] { kv.Key, kv.Value }));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        wb.SaveAs(path);
    }

    private static void AddSheet(XLWorkbook wb, string name, string[] headers, IEnumerable<object?[]> rows)
    {
        var ws = wb.AddWorksheet(name);
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
                ws.Cell(r, c + 1).Value = row[c] switch
                {
                    null => Blank.Value,
                    string str => str,
                    int i => i,
                    decimal d => d,
                    double d => d,
                    bool b => b,
                    var other => other.ToString()
                };
            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
    }
}

/// <summary>The rows of one workbook, as the API sees them.</summary>
public sealed record WorkbookContents(
    IReadOnlyList<Company> Companies,
    IReadOnlyList<CompanyLocation> Locations,
    IReadOnlyList<FinancialPeriod> Financials,
    IReadOnlyList<ExecutiveCompensation> Pay,
    IReadOnlyList<Person> People,
    DataSetMetadata Metadata);
