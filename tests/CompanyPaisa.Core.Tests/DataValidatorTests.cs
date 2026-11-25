using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data;
using CompanyPaisa.Importer.Validation;

namespace CompanyPaisa.Core.Tests;

public class DataValidatorTests
{
    private static readonly Dictionary<string, decimal> Rates = new() { ["USD"] = 1m, ["EUR"] = 1.08m };
    private static readonly DateOnly Today = new(2026, 9, 14);

    private static Company Co(string id, string currency = "USD") =>
        new()
        {
            CompanyId = id, Name = $"{id} Corp", Ticker = id.Split('.')[0], Exchange = "NYSE", Sector = "Other", Currency = currency,
            Classification = new IndustryClassification { Scheme = "SIC", Code = "7372", Name = "Services-Prepackaged Software", Source = "SEC EDGAR", CheckedOn = Today }
        };

    private static CompanyLocation Hq(string id, string state, double lat, double lng) =>
        new() { LocationId = $"{id}-HQ", CompanyId = id, Type = LocationType.Headquarters, Label = "Headquarters", City = "Somewhere", State = state, Point = new GeoPoint(lat, lng) };

    private static FinancialPeriod Year(string id, int year, decimal revenue, decimal netIncome = 1m) =>
        new() { CompanyId = id, PeriodType = PeriodType.Annual, FiscalYear = year, Revenue = revenue, NetIncome = netIncome };

    private static ExecutiveCompensation Pay(string id, string name, int year, decimal salary, decimal total) =>
        new() { CompanyId = id, PersonId = name.ToLowerInvariant().Replace(' ', '-'), ExecutiveName = name, Title = "CEO", Year = year, Salary = salary, Other = Math.Max(0, total - salary), Total = total };

    private static CompanyData Data(Company[] companies, CompanyLocation[] locations, FinancialPeriod[]? financials = null, ExecutiveCompensation[]? pay = null) =>
        new(companies, locations, financials ?? companies.Select(c => Year(c.CompanyId, 2025, 1_000_000m)).ToArray(), pay ?? [],
            (pay ?? []).Select(p => new Person { PersonId = p.PersonId, Name = p.ExecutiveName }).DistinctBy(p => p.PersonId).ToList(),
            new DataSetMetadata("test", Today, false, DateTimeOffset.UtcNow));

    private static int Failures(CompanyData data, string check) =>
        DataValidator.Validate(data, Rates, Today).Checks.Single(c => c.Name == check).Count;

    [Fact]
    public void A_company_without_a_category_or_with_an_old_one_is_flagged()
    {
        var old = Co("OLD") with { Classification = Co("OLD").Classification! with { CheckedOn = Today.AddDays(-200) } };
        var data = Data([Co("AAA") with { Classification = null }, old], [Hq("AAA", "UT", 40.4, -111.9), Hq("OLD", "UT", 40.4, -111.9)]);
        Assert.Equal(1, Failures(data, "No industry classification"));
        Assert.Equal(1, Failures(data, "Classification not checked recently"));
    }

    [Fact]
    public void Clean_data_passes_every_check()
    {
        var data = Data([Co("AAA")], [Hq("AAA", "UT", 40.4, -111.9)], [Year("AAA", 2024, 90_000_000m), Year("AAA", 2025, 100_000_000m)],
            [Pay("AAA", "Jane Smith", 2025, 500_000m, 2_000_000m)]);

        var result = DataValidator.Validate(data, Rates, Today);

        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Warnings);
    }

    [Fact]
    public void Flags_a_location_outside_its_country() =>
        // A US company whose point landed abroad (a foreign postcode read as a ZIP).
        Assert.Equal(1, Failures(Data([Co("BBB")], [Hq("BBB", "IN", 32.1, 34.8)]), "Outside its country"));

    [Fact]
    public void Tells_european_companies_from_canadian_provinces_by_ticker_suffix() =>
        // "NL" is both Newfoundland and the Netherlands; the .AS suffix says Amsterdam.
        Assert.Equal(0, Failures(Data([Co("ASML.AS", "EUR")], [Hq("ASML.AS", "NL", 51.4, 5.4)]), "Outside its country"));

    [Fact]
    public void A_tenfold_fall_to_a_small_figure_is_flagged_but_a_year_without_sales_is_not()
    {
        // Tiptree: $341 million to $0.5 million (the import's measure changed). A drug developer's first sales are not judged.
        var data = Data([Co("TIPT"), Co("BIO")], [Hq("TIPT", "NY", 40.7, -74.0), Hq("BIO", "MA", 42.4, -71.1)],
            [Year("TIPT", 2024, 341_469_000), Year("TIPT", 2025, 488_000), Year("BIO", 2024, 0), Year("BIO", 2025, 45_000_000)]);

        Assert.Equal(1, Failures(data, "Revenue jumps 10× in a year"));
    }

    [Fact]
    public void Flags_impossible_financials()
    {
        var data = Data([Co("CCC")], [Hq("CCC", "TX", 32.8, -96.8)], [Year("CCC", 2025, -5m), Year("CCC", 2027, 10m)]);
        Assert.Equal(1, Failures(data, "Negative revenue"));   // a warning: reported negative revenue can be real
        Assert.Equal(1, Failures(data, "Period in the future"));
    }

    [Fact]
    public void Flags_pay_rows_that_cannot_be_right()
    {
        var data = Data([Co("DDD")], [Hq("DDD", "CA", 37.4, -122.1)], pay:
        [
            Pay("DDD", "Mark Tryniski", 2022, 887_337m, 349_619m),   // salary above the total
            Pay("DDD", "Ben Taecker", 2023, 0m, 873m),                // a table in thousands read as dollars
            Pay("DDD", "Gap Inc.", 2023, 1m, 50_000m)                  // a company in the name column
        ]);
        Assert.Equal(1, Failures(data, "Salary bigger than total"));
        Assert.Equal(1, Failures(data, "Tiny total"));
        Assert.Equal(1, Failures(data, "Name doesn't look like a person"));
    }

    [Fact]
    public void A_pay_total_bigger_than_any_ever_reported_is_an_error()
    {
        // A company tagged $10,990,318 with a "millions" scale: $10,990bn.
        var data = Data([Co("SAIC")], [Hq("SAIC", "VA", 38.9, -77.4)], pay: [Pay("SAIC", "Toni Townes-Whitley", 2024, 1_000_000m, 10_990_318_000_000m)]);
        var result = DataValidator.Validate(data, Rates, Today);
        Assert.Equal(1, result.Checks.Single(c => c.Name == "Impossibly large total").Count);
        Assert.True(result.Errors > 0);
    }

    [Fact]
    public void Flags_a_currency_the_site_has_no_rate_for() =>
        Assert.Equal(1, Failures(Data([Co("SIM", "MXN")], [Hq("SIM", "TX", 29.4, -98.5)]), "Currency without an exchange rate"));

    [Theory]
    [InlineData("Steven R. Fife", true)]
    [InlineData("Joseph W. Craft III", true)]
    [InlineData("Officers as a", false)]
    [InlineData("Charles C. Brockett Company", false)]
    [InlineData("Madonna", false)]
    public void Recognises_person_names(string name, bool person) =>
        Assert.Equal(person, DataValidator.LooksLikePerson(name));
}
