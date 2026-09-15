using System.Globalization;
using ClosedXML.Excel;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Data.Excel;

/// <summary>
/// Reads the CompanyPaisa workbook. Columns are matched by header name (any order, case-insensitive),
/// so people can add helper columns in Excel without breaking the import.
/// </summary>
internal static class ExcelWorkbookReader
{
    /// <summary>The workbook's rows; unreadable cells are reported (with sheet and row) as a <see cref="DataLoadException"/>.</summary>
    public static CompanyData Read(string path, ExcelSheetNames sheets, DateTimeOffset loadedAt)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Workbook not found at '{path}'. Check DataSource:Excel:Path in appsettings.", path);

        // FileShare.ReadWrite lets us read while the file is open in Excel.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var problems = new List<string>();

        var companies = ReadSheet(workbook, sheets.Companies, problems, r => new Company
        {
            CompanyId = r.Required("company_id"),
            Name = r.Required("name"),
            Ticker = r.Required("ticker").ToUpperInvariant(),
            Exchange = r.Required("exchange"),
            Sector = r.Required("sector"),
            Industry = r.Text("industry"),
            Website = r.Text("website"),
            Employees = r.Int("employees"),
            MarketCap = r.Decimal("market_cap"),
            Description = r.Text("description"),
            Currency = r.Text("currency") ?? "USD",
            PayCurrency = r.Text("pay_currency"),
            FiscalYearEnd = r.Text("fiscal_year_end"),
            LogoUrl = r.Text("logo_url"),
            AsOfDate = r.Date("as_of_date")
        });

        var locations = ReadSheet(workbook, sheets.Locations, problems, r => new CompanyLocation
        {
            LocationId = r.Required("location_id"),
            CompanyId = r.Required("company_id"),
            Type = r.Enum<LocationType>("type"),
            Label = r.Required("label"),
            Street = r.Text("street") ?? "",
            City = r.Required("city"),
            State = r.Required("state"),
            PostalCode = r.Text("postal_code") ?? "",
            Point = r.Point("latitude", "longitude")
        });

        var financials = ReadSheet(workbook, sheets.Financials, problems, r => new FinancialPeriod
        {
            CompanyId = r.Required("company_id"),
            PeriodType = r.Enum<PeriodType>("period_type"),
            FiscalYear = r.Int("fiscal_year") ?? throw r.Problem("fiscal_year", "is required"),
            FiscalQuarter = r.Int("fiscal_quarter"),
            Revenue = r.Decimal("revenue") ?? throw r.Problem("revenue", "is required"),
            NetIncome = r.Decimal("net_income") ?? throw r.Problem("net_income", "is required"),
            OperatingIncome = r.Decimal("operating_income"),
            Eps = r.Decimal("eps"),
            SourceFiling = r.Text("source_filing")
        });

        var executives = ReadSheet(workbook, sheets.ExecutiveCompensation, problems, r => new ExecutiveCompensation
        {
            CompanyId = r.Required("company_id"),
            // person_id links one person across companies; older workbooks only had a per-company exec_id.
            PersonId = r.Text("person_id") ?? r.Required("exec_id"),
            ExecutiveName = r.Required("exec_name"),
            Title = r.Required("title"),
            Year = r.Int("year") ?? throw r.Problem("year", "is required"),
            Salary = r.Decimal("salary") ?? 0,
            Bonus = r.Decimal("bonus") ?? 0,
            StockAwards = r.Decimal("stock_awards") ?? 0,
            Other = r.Decimal("other") ?? 0,
            Total = r.Decimal("total") ?? (r.Decimal("salary") ?? 0) + (r.Decimal("bonus") ?? 0) + (r.Decimal("stock_awards") ?? 0) + (r.Decimal("other") ?? 0),
            SourceFiling = r.Text("source_filing")
        }, required: false);

        var people = ReadSheet(workbook, sheets.People, problems, r => new Person
        {
            PersonId = r.Required("person_id"),
            Name = r.Required("name"),
            SecCik = r.Text("sec_cik")
        }, required: false);

        if (problems.Count > 0) throw new DataLoadException(path, problems);
        return new CompanyData(companies, locations, financials, executives, people, ReadMeta(workbook, sheets.Meta, loadedAt));
    }

    private static DataSetMetadata ReadMeta(XLWorkbook workbook, string sheetName, DateTimeOffset loadedAt)
    {
        if (!workbook.TryGetWorksheet(sheetName, out var sheet)) return new DataSetMetadata("unversioned", null, false, loadedAt);
        var values = sheet.RowsUsed().Skip(1)
            .ToDictionary(r => r.Cell(1).GetString().Trim().ToLowerInvariant(), r => r.Cell(2).GetString().Trim());
        return new DataSetMetadata(
            values.GetValueOrDefault("data_version", "unversioned"),
            DateOnly.TryParse(values.GetValueOrDefault("as_of_date"), CultureInfo.InvariantCulture, out var d) ? d : null,
            bool.TryParse(values.GetValueOrDefault("is_sample"), out var s) && s,
            loadedAt);
    }

    private static List<T> ReadSheet<T>(XLWorkbook workbook, string sheetName, List<string> problems, Func<Row, T> map, bool required = true)
    {
        if (!workbook.TryGetWorksheet(sheetName, out var sheet))
        {
            if (required) problems.Add($"Sheet '{sheetName}' is missing.");
            return [];
        }

        var rows = sheet.RowsUsed().ToList();
        if (rows.Count == 0) return [];
        var header = rows[0].CellsUsed().ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber);

        var result = new List<T>();
        foreach (var xlRow in rows.Skip(1))
        {
            if (xlRow.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString()))) continue;
            try { result.Add(map(new Row(sheetName, xlRow, header))); }
            catch (RowProblem p) { problems.Add(p.Message); }
        }
        return result;
    }

    private sealed class RowProblem(string message) : Exception(message);

    /// <summary>Typed access to one spreadsheet row by column name.</summary>
    private sealed class Row(string sheet, IXLRow row, IReadOnlyDictionary<string, int> header)
    {
        public Exception Problem(string column, string issue) =>
            new RowProblem($"{sheet} row {row.RowNumber()}: '{column}' {issue}.");

        private IXLCell? Cell(string column) => header.TryGetValue(column, out var n) ? row.Cell(n) : null;

        public string? Text(string column) => Cell(column)?.GetString().Trim() is { Length: > 0 } s ? s : null;

        public string Required(string column) => Text(column) ?? throw Problem(column, "is required");

        public int? Int(string column) => Decimal(column) is { } d ? (int)d : null;

        public decimal? Decimal(string column)
        {
            var cell = Cell(column);
            if (cell is null || cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
            return decimal.TryParse(cell.GetString().Replace("$", "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                ? v : throw Problem(column, $"must be a number (found '{cell.GetString()}')");
        }

        public DateOnly? Date(string column)
        {
            var cell = Cell(column);
            if (cell is null || cell.IsEmpty()) return null;
            if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
            return DateOnly.TryParse(cell.GetString(), CultureInfo.InvariantCulture, out var d) ? d : throw Problem(column, "must be a date");
        }

        public TEnum Enum<TEnum>(string column) where TEnum : struct, System.Enum =>
            System.Enum.TryParse<TEnum>(Required(column).Replace(" ", ""), ignoreCase: true, out var v)
                ? v : throw Problem(column, $"must be one of {string.Join(", ", System.Enum.GetNames<TEnum>())}");

        public GeoPoint Point(string latColumn, string lngColumn)
        {
            var lat = Decimal(latColumn) ?? throw Problem(latColumn, "is required");
            var lng = Decimal(lngColumn) ?? throw Problem(lngColumn, "is required");
            return new GeoPoint((double)lat, (double)lng);
        }
    }
}
