using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data;
using CompanyPaisa.Importer.Validation;

namespace CompanyPaisa.Core.Tests;

public class EdgarCrossCheckTests
{
    private static readonly Dictionary<string, decimal> Rates = new() { ["USD"] = 1m };

    private static CompanyData Data(params FinancialPeriod[] financials) =>
        new([new Company { CompanyId = "GS", Name = "Goldman Sachs", Ticker = "GS", Exchange = "NYSE", Sector = "Financials", Currency = "USD" },
             new Company { CompanyId = "WMT", Name = "Walmart", Ticker = "WMT", Exchange = "NYSE", Sector = "Retail", Currency = "USD" }],
            [], financials, [], [], new DataSetMetadata("test", new DateOnly(2026, 11, 9), false, DateTimeOffset.UtcNow));

    private static FinancialPeriod Year(string id, int year, decimal revenue, decimal netIncome) =>
        new() { CompanyId = id, PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = netIncome };

    private static EdgarReport Report(string id, params EdgarPeriod[] periods) => new(id, "ok", "10-K", "0000-26-1", null, periods);

    [Fact]
    public void Flags_a_revenue_far_from_the_annual_report_and_passes_one_that_agrees()
    {
        // Goldman before the bank fix: gross interest income + non-interest income against "Total net revenues".
        var data = Data(Year("GS", 2025, 125_097_000_000m, 17_176_000_000m), Year("GS", 2024, 53_512_000_000m, 14_276_000_000m));
        var reports = new[] { Report("GS", new EdgarPeriod(new DateOnly(2025, 12, 31), 58_283_000_000m, 17_176_000_000m),
                                            new EdgarPeriod(new DateOnly(2024, 12, 31), 53_600_000_000m, 14_276_000_000m)) };

        var result = EdgarCrossCheck.Compare(data, reports, Rates);

        Assert.Equal(1, result.Checks.Single(c => c.Name == "Revenue differs from the annual report").Count);
        Assert.Equal(0, result.Checks.Single(c => c.Name == "Net income differs from the annual report").Count);
        Assert.Equal(4, result.Comparisons.Count);
        Assert.All(result.Checks, c => Assert.Equal(Severity.Warning, c.Severity));
    }

    [Fact]
    public void A_year_ending_in_january_is_matched_to_the_previous_fiscal_year()
    {
        // Walmart's year to 31 January 2026 is our fiscal 2025, as in the SEC import.
        var data = Data(Year("WMT", 2025, 713_163_000_000m, 21_893_000_000m));
        var reports = new[] { Report("WMT", new EdgarPeriod(new DateOnly(2026, 1, 31), 713_163_000_000m, 21_893_000_000m)) };

        var result = EdgarCrossCheck.Compare(data, reports, Rates);

        Assert.Empty(result.OnlyInReport);
        Assert.All(result.Comparisons, c => Assert.Equal(0m, c.Gap));
    }

    [Fact]
    public void A_year_we_dont_have_is_reported_not_compared()
    {
        var data = Data(Year("GS", 2024, 53_512_000_000m, 14_276_000_000m));
        var reports = new[] { Report("GS", new EdgarPeriod(new DateOnly(2025, 12, 31), 58_283_000_000m, 17_176_000_000m)) };

        var result = EdgarCrossCheck.Compare(data, reports, Rates);

        Assert.Equal([("GS", 2025)], result.OnlyInReport);
        Assert.Empty(result.Comparisons);
    }

    [Fact]
    public void Loads_the_files_extract_py_writes()
    {
        var dir = Directory.CreateTempSubdirectory("edgar-test").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "GS.json"), """
                { "company_id": "GS", "cik": 886982, "edgartools": "5.62.1", "form": "10-K", "accession": "0000886982-26-000091",
                  "periods": [ { "end": "2025-12-31", "revenue": 58283000000, "net_income": 17176000000 },
                               { "end": "2024-12-31", "revenue": null, "net_income": 14276000000 } ],
                  "status": "ok" }
                """);
            File.WriteAllText(Path.Combine(dir, "XYZ.json"), """{ "company_id": "XYZ", "status": "failed", "error": "ParsingError: bad", "periods": [] }""");

            var reports = EdgarCrossCheck.Load(dir);

            var gs = reports.Single(r => r.CompanyId == "GS");
            Assert.Equal(58_283_000_000m, gs.Periods[0].Revenue);
            Assert.Null(gs.Periods[1].Revenue);
            Assert.Equal("failed", reports.Single(r => r.CompanyId == "XYZ").Status);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Nothing_to_load_when_the_extractor_has_not_run() =>
        Assert.Empty(EdgarCrossCheck.Load(Path.Combine(Path.GetTempPath(), "no-such-edgar-dir-" + Guid.NewGuid())));
}
