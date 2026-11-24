using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

public class FiscalYearEndTests
{
    [Theory]
    [InlineData("1231", "2025-06-30", "06-30")]   // CACI: EDGAR says December, the 10-K's year ends in June
    [InlineData("1231", "2025-05-30", "05-30")]   // Aehr
    [InlineData("0925", "2025-09-27", "09-25")]   // Apple's 52/53-week year: EDGAR's usual day is kept
    [InlineData("1231", "2025-12-31", "12-31")]
    [InlineData("0131", "2026-01-31", "01-31")]
    [InlineData(null, "2025-03-31", "03-31")]
    [InlineData("0630", null, "06-30")]
    [InlineData("1231", "2026-01-03", "12-31")]   // a 53-week year ending in early January
    [InlineData("1226", "2026-01-03", "12-26")]   // Onto Innovation: EDGAR's usual December day is kept
    [InlineData(null, "2026-01-03", "12-31")]
    public void The_annual_reports_year_end_wins_when_EDGAR_disagrees(string? edgar, string? reported, string expected) =>
        Assert.Equal(expected, FiscalYears.YearEnd(edgar, reported is null ? null : DateOnly.Parse(reported)));
}
