using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

public class StatementYearsTests
{
    // L3Harris: company facts stop at 2024; the 10-K for the year to 2 January 2026 prints both years.
    private static List<int> Years(decimal priorRevenue = 21_325m, decimal priorProfit = 1_502m, string label = "Revenue",
        decimal? total = null, string? note = null)
    {
        var ours = new FinancialPeriod { CompanyId = "LHX", PeriodType = PeriodType.Annual, FiscalYear = 2024, Revenue = 21_325m, NetIncome = 1_502m };
        var statement = new StatementReading("A-26-1", "2026-02-12",
        [
            new StatementYear(new DateOnly(2026, 1, 2), 21_865m, 1_606m, label, "Revenues", total),
            new StatementYear(new DateOnly(2025, 1, 3), priorRevenue, priorProfit, label, "Revenues", total),
        ]);
        return StatementYears.AddNewest(new FinancialsResult([ours], null, note is null ? [] : [note]), 202058, 10, statement)
            .Periods.Select(p => p.FiscalYear).ToList();
    }

    [Fact]
    public void The_newest_year_is_added_when_the_prior_column_matches_ours() => Assert.Equal([2024, 2025], Years());

    [Fact]
    public void Another_revenue_measure_is_not_added() => Assert.Equal([2024], Years(priorRevenue: 19_000m));

    [Fact]
    public void Another_profit_measure_is_not_added() => Assert.Equal([2024], Years(priorProfit: 1_400m));

    [Fact]
    public void A_sub_line_is_not_added() => Assert.Equal([2024], Years(label: "License and other revenue"));

    [Fact]
    public void A_statement_with_a_larger_total_is_not_added() => Assert.Equal([2024], Years(total: 30_000m));

    [Fact]
    public void A_bank_is_not_added() =>
        Assert.Equal([2024], Years(note: "Revenue = net interest income + non-interest income (bank)."));
}
