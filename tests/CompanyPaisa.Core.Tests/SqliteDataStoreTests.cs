using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data;
using CompanyPaisa.Data.Sqlite;

namespace CompanyPaisa.Core.Tests;

public sealed class SqliteDataStoreTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"cp-store-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var f in new[] { _db, _db + ".new" }) if (File.Exists(f)) File.Delete(f);
    }

    private static CompanyData Market(string id, decimal revenue, string filing, string? person = null) => new(
        [new Company { CompanyId = id, Name = $"{id} Inc.", Ticker = id, Exchange = "NYSE", Sector = "Other", Currency = "USD" }],
        [new CompanyLocation { LocationId = $"{id}-HQ", CompanyId = id, Type = LocationType.Headquarters, Label = "Headquarters", City = "Lehi", State = "UT", Point = new GeoPoint(40.39, -111.85) }],
        [
            new FinancialPeriod { CompanyId = id, PeriodType = PeriodType.Annual, FiscalYear = 2025, Revenue = revenue, NetIncome = -1_234_567m, Eps = 1.37m, SourceFiling = filing },
            new FinancialPeriod { CompanyId = id, PeriodType = PeriodType.Quarterly, FiscalYear = 2025, FiscalQuarter = 4, Revenue = revenue / 4, NetIncome = 5m, SourceFiling = filing }
        ],
        person is null ? [] : [new ExecutiveCompensation { CompanyId = id, PersonId = person, ExecutiveName = "Jane Smith", Title = "CEO", Year = 2025, Salary = 900_000m, Other = 100_000m, Total = 1_000_000m, SourceFiling = filing }],
        person is null ? [] : [new Person { PersonId = person, Name = "Jane Smith", SecCik = "0001234567" }],
        new DataSetMetadata("test", null, false, DateTimeOffset.UtcNow));

    private static Dictionary<string, string> Meta(string version) => new() { ["data_version"] = version, ["as_of_date"] = "2026-09-15", ["is_sample"] = "false" };

    [Fact]
    public void Round_trips_amounts_links_and_people_exactly()
    {
        const string filing = "https://www.sec.gov/Archives/edgar/data/320193/000032019325000079/";
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 416_161_000_000m, filing, "jane-smith-1"), Meta("sec-1"));

        var data = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);

        var annual = data.Financials.Single(f => f.PeriodType == PeriodType.Annual);
        Assert.Equal((416_161_000_000m, -1_234_567m, 1.37m, filing), (annual.Revenue, annual.NetIncome, annual.Eps!.Value, annual.SourceFiling));
        Assert.Equal(4, data.Financials.Single(f => f.PeriodType == PeriodType.Quarterly).FiscalQuarter);
        Assert.Equal(1_000_000m, Assert.Single(data.Pay).Total);
        Assert.Equal("0001234567", Assert.Single(data.People).SecCik);
        Assert.Equal(new GeoPoint(40.39, -111.85), Assert.Single(data.Locations).Point);
    }

    [Fact]
    public void Keeps_pay_ratios_with_their_market_and_job_salaries_across_imports()
    {
        const string proxy = "https://www.sec.gov/Archives/edgar/data/320193/000130817926000008/aapl4359751-def14a.htm";
        var sec = Market("AAPL", 100m, proxy) with
        {
            WorkerPayRows = [new WorkerPay { CompanyId = "AAPL", Year = 2025, MedianEmployeePay = 114_738m, CeoPay = 74_294_811m, Ratio = 647.5m, SourceFiling = proxy }]
        };
        SqliteDataStore.ReplaceMarket(_db, "sec", sec, Meta("sec-1"));
        SqliteDataStore.ReplaceJobSalaries(_db,
        [
            new JobSalary { CompanyId = "AAPL", Title = "Software Engineer", Occupation = "Software Developers", Filings = 40, Min = 120_000m, Low = 150_000m, Median = 175_000m, High = 200_000m, Max = 260_000m },
            new JobSalary { CompanyId = "AAPL", Title = "Software Engineer", City = "Cupertino", State = "CA", Point = new GeoPoint(37.32, -122.03), Filings = 30, Min = 130_000m, Low = 160_000m, Median = 180_000m, High = 205_000m, Max = 260_000m },
            new JobSalary { CompanyId = "GONE", Title = "Analyst", Filings = 3, Min = 1, Low = 1, Median = 1, High = 1, Max = 1 }   // not a company here: dropped
        ], new JobSalarySource(new DateOnly(2024, 10, 1), new DateOnly(2026, 6, 30), "US Department of Labor"));
        SqliteDataStore.ReplaceJobSalaries(_db,
        [
            new JobSalary { CompanyId = "AAPL", Source = JobSalary.JobAds, Title = "Retail Specialist", Url = "https://jobs.apple.com/1", Filings = 12, Min = 38_000m, Low = 40_000m, Median = 45_000m, High = 50_000m, Max = 55_000m }
        ], new JobSalarySource(new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 22), "Job ads", JobSalary.JobAds));

        // Another import of the market keeps the salaries (they aren't a market's rows) and replaces the ratio.
        SqliteDataStore.ReplaceMarket(_db, "sec", sec, Meta("sec-2"));
        var data = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);

        var ratio = Assert.Single(data.WorkerPays);
        Assert.Equal((114_738m, 74_294_811m, 647.5m, proxy), (ratio.MedianEmployeePay, ratio.CeoPay, ratio.Ratio, ratio.SourceFiling));
        Assert.Equal(3, data.JobSalaries.Count);
        Assert.Equal("https://jobs.apple.com/1", data.JobSalaries.Single(j => j.Source == JobSalary.JobAds).Url);
        Assert.Equal(new GeoPoint(37.32, -122.03), data.JobSalaries.Single(j => j.City == "Cupertino").Point);
        Assert.Equal(new DateOnly(2026, 6, 30), data.SalarySources.Single(s => s.Kind == JobSalary.VisaFilings).To);
        Assert.Equal(new DateOnly(2026, 9, 22), data.SalarySources.Single(s => s.Kind == JobSalary.JobAds).To);

        // A company leaving the market takes its salaries with it.
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("MSFT", 100m, proxy), Meta("sec-3"));
        Assert.Empty(SqliteDataStore.Read(_db, DateTimeOffset.UtcNow).JobSalaries);
    }

    [Fact]
    public void Hand_corrections_apply_as_the_data_loads_and_outlive_a_reimport()
    {
        const string report = "https://www.sec.gov/Archives/edgar/data/1856031/000119312526103023/";
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("SEAT", 647_891_000m, "https://www.sec.gov/Archives/edgar/data/1856031/a/"), Meta("sec-1"));
        SqliteDataStore.ReplaceCorrections(_db, [new Core.Services.FinancialCorrection("seat", 2025, 775_586_000m, null, report, "Total revenues")]);
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("SEAT", 647_891_000m, "https://www.sec.gov/Archives/edgar/data/1856031/a/"), Meta("sec-2"));

        var annual = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow).Financials.Single(f => f.PeriodType == PeriodType.Annual);

        Assert.Equal((775_586_000m, -1_234_567m, report), (annual.Revenue, annual.NetIncome, annual.SourceFiling));
        Assert.Single(SqliteDataStore.ReadCorrections(_db));
    }

    [Fact]
    public void Replacing_one_market_leaves_the_others_alone()
    {
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 100m, "https://www.sec.gov/Archives/edgar/data/1/a/"), Meta("sec-1"));
        SqliteDataStore.ReplaceMarket(_db, "uk", Market("TSCO.L", 200m, "https://filings.xbrl.org/x/report"), Meta("uk-1"));
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("MSFT", 300m, "https://www.sec.gov/Archives/edgar/data/2/b/"), Meta("sec-2"));

        var data = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);

        Assert.Equal(["MSFT", "TSCO.L"], data.Companies.Select(c => c.CompanyId).Order());
        Assert.Equal("sec-2+uk-1", data.Metadata.DataVersion);
        Assert.Equal(new DateOnly(2026, 9, 15), data.Metadata.AsOfDate);
    }

    [Fact]
    public void The_data_is_as_new_as_its_newest_market_and_keeps_each_ones_date()
    {
        SqliteDataStore.ReplaceMarket(_db, "pk", Market("OGDC.KA", 100m, "https://example.test/report"), Meta("pk-1"));
        var meta = Meta("sec-1");
        meta["as_of_date"] = "2026-11-22";
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 100m, "https://www.sec.gov/Archives/edgar/data/1/a/"), meta);

        var data = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);

        Assert.Equal(new DateOnly(2026, 11, 22), data.Metadata.AsOfDate);
        Assert.Equal(new DateOnly(2026, 9, 15), data.Metadata.MarketDates!["pk"]);
        Assert.Equal("pk", data.Companies.Single(c => c.CompanyId == "OGDC.KA").Market);
    }

    [Fact]
    public void A_changed_ticker_still_leads_to_the_company()
    {
        void Publish(string id, int cik) =>
            SqliteDataStore.ReplaceMarket(_db, "sec", Market(id, 100m, $"https://www.sec.gov/Archives/edgar/data/{cik}/a/"), Meta("sec-1"));
        string? Find(string id) => new DataSnapshot(SqliteDataStore.Read(_db, DateTimeOffset.UtcNow)).Find(id)?.Ticker;

        Publish("TCBI", 7);
        Publish("TXCP", 7);
        Assert.Equal("TXCP", Find("tcbi"));

        Publish("TXC", 7);   // renamed again: both old addresses follow
        Assert.Equal(("TXC", "TXC"), (Find("TCBI"), Find("TXCP")));

        Publish("OTHER", 8); // another company taking its place is not a rename
        Assert.Null(Find("TXC"));
    }

    [Fact]
    public void A_company_matched_to_anothers_filer_does_not_redirect_to_it()   // B&M and Mitchells & Butlers
    {
        const string filing = "https://filings.xbrl.org/SAMELEI/report";
        var both = Market("MAB.L", 100m, filing);
        var wrong = Market("BME.L", 100m, filing);
        both = both with { Companies = [.. both.Companies, .. wrong.Companies], Locations = [.. both.Locations, .. wrong.Locations], Financials = [.. both.Financials, .. wrong.Financials] };
        SqliteDataStore.ReplaceMarket(_db, "uk", both, Meta("uk-1"));
        SqliteDataStore.ReplaceMarket(_db, "uk", Market("MAB.L", 100m, filing), Meta("uk-2"));

        Assert.Empty(SqliteDataStore.Read(_db, DateTimeOffset.UtcNow).FormerTickers);
    }

    [Fact]
    public void A_ticker_in_use_again_is_no_longer_a_former_ticker()
    {
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("OLD", 100m, "https://www.sec.gov/Archives/edgar/data/7/a/"), Meta("sec-1"));
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("NEW", 100m, "https://www.sec.gov/Archives/edgar/data/7/a/"), Meta("sec-2"));
        SqliteDataStore.ReplaceMarket(_db, "uk", Market("OLD", 100m, "https://filings.xbrl.org/x/report"), Meta("uk-1"));

        var data = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);

        Assert.Empty(data.FormerTickers);
        Assert.Equal("OLD", new DataSnapshot(data).Find("OLD")!.Ticker);
    }

    [Fact]
    public void A_failed_publish_leaves_the_existing_file_untouched()
    {
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 100m, "https://www.sec.gov/Archives/edgar/data/1/a/"), Meta("sec-1"));
        var before = File.ReadAllBytes(_db);

        // Another market claiming the same company id breaks the primary key.
        Assert.ThrowsAny<Exception>(() =>
            SqliteDataStore.ReplaceMarket(_db, "uk", Market("AAPL", 999m, "https://filings.xbrl.org/x/report"), Meta("uk-1")));

        Assert.Equal(before, File.ReadAllBytes(_db));
    }

    [Fact]
    public void Appointments_round_trip_and_can_be_replaced_on_their_own()
    {
        const string filing = "https://www.sec.gov/Archives/edgar/data/849146/000119312526159324/lfvn-20260413.htm";
        var hire = new NewExecutive
        {
            CompanyId = "AAPL", PersonId = "terrence-moorehead-1754431", Name = "Terrence O. Moorehead", Title = "President and Chief Executive Officer",
            AnnouncedOn = new DateOnly(2026, 4, 16), StartsOn = new DateOnly(2026, 8, 5), SourceFiling = filing,
            Package = [new(PackageItemKind.Salary, 850_000, "Base salary"), new(PackageItemKind.PerformanceStock, 3_500_000, "Performance stock")]
        };
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 100m, "https://www.sec.gov/Archives/edgar/data/1/a/") with { NewExecutives = [hire] }, Meta("sec-1"));

        var read = Assert.Single(SqliteDataStore.Read(_db, DateTimeOffset.UtcNow).Appointments);
        Assert.Equal((hire.Name, hire.PersonId, hire.StartsOn, filing, 4_350_000m), (read.Name, read.PersonId, read.StartsOn, read.SourceFiling, read.Total));
        Assert.Equal(PackageItemKind.PerformanceStock, read.Package[1].Kind);

        // The quicker import step swaps the appointments only, leaving companies and pay alone.
        SqliteDataStore.ReplaceNewExecutives(_db, "sec", [hire with { Name = "Jane Q. Doe", PersonId = null, StartsOn = null }]);
        var after = SqliteDataStore.Read(_db, DateTimeOffset.UtcNow);
        Assert.Equal("Jane Q. Doe", Assert.Single(after.Appointments).Name);
        Assert.Equal(["AAPL"], after.Companies.Select(c => c.CompanyId));
        Assert.Throws<DataLoadException>(() => SqliteDataStore.ReplaceNewExecutives(_db, "sec", [hire with { CompanyId = "NOPE" }]));
    }

    [Fact]
    public void Data_the_website_would_reject_is_never_published()
    {
        SqliteDataStore.ReplaceMarket(_db, "sec", Market("AAPL", 100m, "https://www.sec.gov/Archives/edgar/data/1/a/"), Meta("sec-1"));
        var bad = Market("MSFT", 1m, "https://www.sec.gov/Archives/edgar/data/2/b/");
        bad = bad with { Financials = [.. bad.Financials, bad.Financials[1] with { FiscalQuarter = 7 }] };   // quarter 7

        Assert.Throws<DataLoadException>(() => SqliteDataStore.ReplaceMarket(_db, "uk", bad, Meta("uk-1")));
        Assert.Equal(["AAPL"], SqliteDataStore.Read(_db, DateTimeOffset.UtcNow).Companies.Select(c => c.CompanyId));
    }
}
