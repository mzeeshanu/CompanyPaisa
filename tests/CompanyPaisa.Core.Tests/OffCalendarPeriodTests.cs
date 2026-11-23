using System.Text.Json;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Financials;

namespace CompanyPaisa.Core.Tests;

/// <summary>Periods are keyed by their own dates, not by the SEC's calendar frames, which lose off-calendar periods.</summary>
public class OffCalendarPeriodTests
{
    private static object Fact(string start, string end, decimal value, string? frame, string form = "10-Q", string filed = "2026-01-01") =>
        new { start, end, val = value, form, filed, accn = "A-" + end, frame };

    private static FinancialsResult Extract(object revenue, object profit) =>
        new XbrlFinancialsExtractor().Extract("X", 1, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["facts"] = new Dictionary<string, object>
            {
                ["us-gaap"] = new Dictionary<string, object> { ["Revenues"] = new { units = new { USD = revenue } }, ["NetIncomeLoss"] = new { units = new { USD = profit } } }
            }
        }), 10);

    [Fact]
    public void A_June_year_framed_as_the_calendar_year_before_keeps_both_years()   // Sysco
    {
        object[] Year(decimal a, decimal b) =>
        [
            Fact("2023-07-02", "2024-06-29", a, null, "10-K"),
            Fact("2024-06-30", "2025-06-28", b, "CY2024", "10-K"),
        ];
        var result = Extract(Year(78_844m, 81_370m), Year(1_955m, 1_828m));
        var years = result.Periods.Where(p => p.PeriodType == PeriodType.Annual).ToDictionary(p => p.FiscalYear, p => p.Revenue);
        Assert.Equal(78_844m, years[2024]);
        Assert.Equal(81_370m, years[2025]);
    }

    [Fact]
    public void Costco_style_quarters_get_four_labels_and_a_derived_fourth()
    {
        object[] Periods(decimal q1, decimal q2, decimal q3, decimal year) =>
        [
            Fact("2024-09-02", "2024-11-24", q1, "CY2024Q4"),
            Fact("2024-11-25", "2025-02-16", q2, null),
            Fact("2025-02-17", "2025-05-11", q3, "CY2025Q1"),
            Fact("2024-09-02", "2025-08-31", year, "CY2025", "10-K"),
        ];
        var result = Extract(Periods(62_151m, 63_723m, 63_205m, 275_235m), Periods(1_798m, 1_788m, 1_903m, 8_099m));
        var quarters = result.Periods.Where(p => p.PeriodType == PeriodType.Quarterly)
            .ToDictionary(p => (p.FiscalYear, p.FiscalQuarter!.Value), p => p.Revenue);
        Assert.Equal(62_151m, quarters[(2024, 4)]);
        Assert.Equal(63_723m, quarters[(2025, 1)]);
        Assert.Equal(63_205m, quarters[(2025, 2)]);
        Assert.Equal(275_235m - 62_151m - 63_723m - 63_205m, quarters[(2025, 3)]);
    }

    [Fact]
    public void Kroger_style_sixteen_week_first_quarters_keep_four_labels()
    {
        object[] Periods(decimal q1, decimal q2, decimal q3, decimal year) =>
        [
            Fact("2023-02-05", "2024-02-03", 150_000m, "CY2023", "10-K"),
            Fact("2024-02-04", "2024-05-25", q1, "CY2024Q2"),
            Fact("2024-05-26", "2024-08-17", q2, null),
            Fact("2024-08-18", "2024-11-09", q3, "CY2024Q3"),
            Fact("2024-02-04", "2025-02-01", year, "CY2024", "10-K"),
        ];
        var result = Extract(Periods(45_000m, 34_000m, 33_000m, 147_000m), Periods(900m, 500m, 600m, 2_600m));
        var quarters = result.Periods.Where(p => p.PeriodType == PeriodType.Quarterly)
            .ToDictionary(p => (p.FiscalYear, p.FiscalQuarter!.Value), p => p.Revenue);
        Assert.Equal(45_000m, quarters[(2024, 1)]);
        Assert.Equal(34_000m, quarters[(2024, 2)]);
        Assert.Equal(33_000m, quarters[(2024, 3)]);
        Assert.Equal(35_000m, quarters[(2024, 4)]);
    }

    [Fact]
    public void A_quarter_missing_from_the_feed_comes_from_the_year_to_date()   // Dow, Q2 2026
    {
        object[] Periods(decimal q1, decimal nine, decimal q3) =>
        [
            Fact("2025-01-01", "2025-12-31", 39_970m, "CY2025", "10-K"),
            Fact("2026-01-01", "2026-03-31", q1, "CY2026Q1"),
            Fact("2026-01-01", "2026-09-30", nine, null),
            Fact("2026-07-01", "2026-09-30", q3, "CY2026Q3"),
        ];
        var result = Extract(Periods(9_794m, 33_153m, 11_267m), Periods(-533m, 646m, 458m));
        var q2 = result.Periods.Single(p => p is { PeriodType: PeriodType.Quarterly, FiscalYear: 2026, FiscalQuarter: 2 });
        Assert.Equal(33_153m - 9_794m - 11_267m, q2.Revenue);
        Assert.Equal(646m + 533m - 458m, q2.NetIncome);
    }

    [Fact]
    public void A_year_in_a_quarterly_report_still_yields_to_the_annual_report()   // Comfort Systems
    {
        object[] Year(decimal annual, decimal slip) =>
        [
            Fact("2025-01-01", "2025-12-31", annual, null, "10-K", "2026-02-20"),
            Fact("2025-01-01", "2025-12-31", slip, "CY2025", "10-Q", "2026-04-25"),
        ];
        var result = Extract(Year(9_100m, 1_800m), Year(900m, 900m));
        Assert.Equal(9_100m, result.Periods.Single(p => p.PeriodType == PeriodType.Annual).Revenue);
    }

    [Theory]
    [InlineData("2026-01-31", 2025, 4)]   // a retailer's Q4
    [InlineData("2025-02-01", 2024, 4)]
    [InlineData("2025-05-03", 2025, 1)]
    [InlineData("2025-04-30", 2025, 1)]
    [InlineData("2025-03-31", 2025, 1)]
    [InlineData("2025-04-05", 2025, 1)]   // a 53-week year
    [InlineData("2025-08-31", 2025, 3)]
    [InlineData("2026-01-03", 2025, 4)]
    public void Quarter_labels_match_the_calendar_quarter_mostly_covered(string end, int year, int quarter) =>
        Assert.Equal((year, quarter), XbrlFinancialsExtractor.QuarterLabel(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
}

public class PredecessorFilerTests
{
    private static FinancialPeriod P(PeriodType type, int year, int? quarter, decimal revenue) =>
        new() { CompanyId = "XOM", PeriodType = type, FiscalYear = year, FiscalQuarter = quarter, Revenue = revenue, NetIncome = 1 };

    [Fact]
    public void The_old_filer_fills_what_the_new_one_lacks_and_never_overrides_it()   // ExxonMobil Holdings
    {
        var successor = new FinancialsResult([P(PeriodType.Quarterly, 2025, 3, 85m), P(PeriodType.Quarterly, 2026, 3, 113m)], null, []);
        var predecessor = new FinancialsResult(
            [P(PeriodType.Annual, 2024, null, 340m), P(PeriodType.Annual, 2025, null, 330m), P(PeriodType.Quarterly, 2025, 3, 84m), P(PeriodType.Quarterly, 2026, 1, 80m)], null, []);
        var merged = PredecessorFiler.Merge(successor, predecessor, 34088, 10)!;
        Assert.Equal(85m, merged.Periods.Single(p => p is { PeriodType: PeriodType.Quarterly, FiscalYear: 2025, FiscalQuarter: 3 }).Revenue);
        Assert.Equal(2, merged.Periods.Count(p => p.PeriodType == PeriodType.Annual));
        Assert.Contains(merged.Periods, p => p is { FiscalYear: 2026, FiscalQuarter: 1 });
        Assert.Contains(merged.Notes, n => n.StartsWith(PredecessorFiler.Note, StringComparison.Ordinal));
    }

    [Fact]
    public void A_different_currency_is_not_merged()
    {
        var successor = new FinancialsResult([P(PeriodType.Quarterly, 2026, 3, 1m)], null, []) { Currency = "CAD" };
        var predecessor = new FinancialsResult([P(PeriodType.Annual, 2025, null, 4m)], null, []);
        Assert.Single(PredecessorFiler.Merge(successor, predecessor, 1, 10)!.Periods);
    }
}
