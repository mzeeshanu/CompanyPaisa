using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

public class PublishedYearsTests
{
    private static FinancialPeriod Year(int year, decimal revenue, long cik = 1654795) => new()
    {
        CompanyId = "BBUC", PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = 387m,
        SourceFiling = $"https://www.sec.gov/Archives/edgar/data/{cik}/000162828026022148/",
    };

    private static List<int> Kept(FinancialPeriod published, decimal stated = 27_457m, string currency = "USD", int extracted = 2022) =>
        PublishedYears.Keep(new FinancialsResult([Year(extracted, 57_385m)], null, []), 1654795, 10, [published], currency,
                new StatementReading("A", "2026-03-31", [new StatementYear(new DateOnly(2025, 12, 31), stated, 387m, "Revenues", "Revenue", null)]))
            .Periods.Select(p => p.FiscalYear).ToList();

    [Fact]
    public void A_year_the_sec_feed_lost_is_kept_when_the_annual_report_states_it() =>
        Assert.Equal([2022, 2025], Kept(Year(2025, 27_457m)));

    [Fact]
    public void A_year_the_annual_report_contradicts_is_not_kept() =>
        Assert.Equal([2022], Kept(Year(2025, 27_457m), stated: 20_000m));

    [Fact]
    public void Another_filer_that_had_the_ticker_is_not_kept() =>
        Assert.Equal([2022], Kept(Year(2025, 27_457m, cik: 99)));

    [Fact]
    public void Another_currency_is_not_kept() =>
        Assert.Equal([2022], Kept(Year(2025, 27_457m), currency: "CAD"));

    [Fact]
    public void Nothing_is_kept_when_the_company_did_not_go_backwards() =>
        Assert.Equal([2025], Kept(Year(2025, 27_457m), extracted: 2025));
}
