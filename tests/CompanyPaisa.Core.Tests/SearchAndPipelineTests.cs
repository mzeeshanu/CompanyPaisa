using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Features.Search;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CompanyPaisa.Core.Tests;

public class GetCompaniesNearHandlerTests
{
    private static readonly GeoPoint Lehi = new(40.3916, -111.8508);

    private static FakeRepository Repo() => new FakeRepository()
        .Add("NEAR", "Software", LocationType.Headquarters, 40.4000, -111.8600, 400)                  // ~0.7 mi
        .Add("BIG", "Finance", LocationType.Office, 40.4300, -111.8900, 4000, growth: 0.01m)          // ~3.3 mi
        .Add("FAST", "Software", LocationType.Headquarters, 40.5200, -111.8700, 100, growth: 0.5m)    // ~8.9 mi
        .Add("FAR", "Software", LocationType.Headquarters, 40.7600, -111.8900, 900)                   // ~25 mi
        .AddLocation("FAR", LocationType.Office, 40.4100, -111.8700);                                 // FAR also has a Lehi office (~1.6 mi)

    private static GetCompaniesNearHandler Handler(FakeRepository repo) => new(
        repo,
        new NearbySearchService(repo, new FakeGeoLocator(("84043", Lehi)), new HaversineDistanceCalculator()),
        new FinancialMetricsService(Opt.Monitor(new MetricsOptions())),
        new CurrencyConverter(Opt.Monitor(new CurrencyOptions())),
        new TopPaidCeoService(repo, new CurrencyConverter(Opt.Monitor(new CurrencyOptions())), Opt.Monitor(new BenchmarkOptions
        {
            MedianPay = { ["US"] = new MedianPayOptions { Description = "US full-time worker", AnnualPay = 65_000, Currency = "USD", Period = "2026", Source = "BLS", SourceUrl = "https://www.bls.gov/" } }
        })),
        Opt.Monitor(new SearchOptions { AllowedRadiiMiles = [5, 10, 25, 50] }));

    private static Task<NearbyCompaniesResponse> Search(NearbyCompaniesRequest r, FakeRepository? repo = null) =>
        Handler(repo ?? Repo()).HandleAsync(new GetCompaniesNearQuery(r), CancellationToken.None);

    [Fact]
    public async Task Returns_companies_within_the_radius_with_their_nearest_location()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 5 });

        Assert.Equal(["BIG", "FAR", "NEAR"], result.Items.Select(i => i.Ticker));   // default sort: revenue (4000, 900, 400)
        var far = result.Items.Single(i => i.Ticker == "FAR");
        Assert.Equal(LocationType.Office, far.NearestLocation.Type);                    // the Lehi office, not the SLC HQ
        Assert.False(far.IsHeadquarteredNearby);
        Assert.Equal("Lehi, UT 84043", result.OriginLabel);
    }

    [Fact]
    public async Task Headquartered_only_keeps_companies_whose_HQ_is_in_range()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, HeadquarteredOnly = true });
        Assert.Equal(["NEAR", "FAST"], result.Items.Select(i => i.Ticker));
    }

    [Theory]
    [InlineData(CompanySort.Growth, "FAST")]
    [InlineData(CompanySort.Distance, "NEAR")]
    [InlineData(CompanySort.Revenue, "BIG")]
    public async Task Sorts_by_the_requested_field(CompanySort sort, string first)
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, Sort = sort });
        Assert.Equal(first, result.Items[0].Ticker);
    }

    [Fact]
    public async Task Summary_covers_all_matches_even_when_paged()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, PageSize = 1 });
        Assert.Single(result.Items);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(4, result.Summary.CompanyCount);
        Assert.Equal(2, result.Summary.HeadquarteredCount);
    }

    [Fact]
    public async Task Sector_filter_is_case_insensitive()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, Sector = "finance" });
        Assert.Equal(["BIG"], result.Items.Select(i => i.Ticker));
    }

    [Fact]
    public async Task Top_paid_CEO_is_the_best_paid_chief_executive_of_a_company_based_here()
    {
        var repo = Repo()
            .Pay("P1", "NEAR", "President and Chief Executive Officer", 2024, 2025, 13_000_000)
            .Pay("P2", "FAST", "Chief Operating Officer", 2025, 2025, 50_000_000)          // not a CEO
            .Pay("P3", "FAST", "Former Chief Executive Officer", 2025, 2025, 30_000_000)   // left
            .Pay("P4", "BIG", "CEO", 2025, 2025, 100_000_000);                             // BIG only has an office here

        var ceo = (await Search(new() { Near = "84043", RadiusMiles = 10 }, repo)).Summary.TopPaidCeo;

        Assert.NotNull(ceo);
        Assert.Equal(("P1", "NEAR", 2025, 13_000_000m), (ceo.PersonId, ceo.Ticker, ceo.Year, ceo.TotalPay));
        Assert.Equal("CEO", ceo.Role);
        Assert.NotNull(ceo.MedianWorker);
        Assert.Equal("US", ceo.MedianWorker.Country);
        Assert.Equal(TopPaidCeoService.HoursPerYear * 65_000 / 13_000_000, ceo.MedianWorker.HoursToEarn, 2);   // ~43.8 hours
    }

    [Fact]
    public async Task Top_paid_CEO_ignores_companies_that_stopped_reporting_pay()
    {
        var repo = Repo()
            .Pay("OLD", "NEAR", "Chief Executive Officer", 2019, 2020, 90_000_000)
            .Pay("NEW", "FAST", "Chief Executive Officer", 2024, 2025, 2_000_000);

        var ceo = (await Search(new() { Near = "84043", RadiusMiles = 10 }, repo)).Summary.TopPaidCeo;

        Assert.Equal("NEW", ceo?.PersonId);
    }

    [Fact]
    public async Task No_top_paid_CEO_when_nobody_reported_is_a_chief_executive()
    {
        var repo = Repo().Pay("P1", "NEAR", "Chief Financial Officer", 2025, 2025, 5_000_000);
        Assert.Null((await Search(new() { Near = "84043", RadiusMiles = 10 }, repo)).Summary.TopPaidCeo);
    }

    [Fact]
    public async Task UK_executive_directors_count_when_the_report_names_no_chief_executive()
    {
        var repo = Repo()
            .Pay("UK1", "NEAR", "Executive Director", 2025, 2025, 7_000_000)
            .Pay("US1", "FAST", "Chief Executive Officer", 2025, 2025, 3_000_000);

        var ceo = (await Search(new() { Near = "84043", RadiusMiles = 10 }, repo)).Summary.TopPaidCeo;

        Assert.Equal(("UK1", "executive director"), (ceo?.PersonId, ceo?.Role));
    }

    [Fact]
    public async Task No_top_paid_CEO_without_pay_data()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10 });
        Assert.Null(result.Summary.TopPaidCeo);
    }

    [Theory]
    [InlineData("Chief Executive Officer", true)]
    [InlineData("President & CEO", true)]
    [InlineData("Group Chief Executive", true)]
    [InlineData("Co-CEO", true)]
    [InlineData("Former Chief Executive Officer", false)]
    [InlineData("Deputy Chief Executive", false)]
    [InlineData("Executive Vice President and Chief Financial Officer", false)]
    public void Recognises_chief_executive_titles(string title, bool expected) =>
        Assert.Equal(expected, TopPaidCeoService.IsChiefExecutive(title));

    [Theory]
    [InlineData("UT", "USD", "US")]
    [InlineData("CA", "USD", "US")]    // California
    [InlineData("ON", "CAD", "CA")]
    [InlineData("NL", "CAD", "CA")]    // Newfoundland
    [InlineData("NL", "EUR", "NL")]    // the Netherlands
    [InlineData("UK", "GBP", "UK")]
    [InlineData("AU", "AUD", "AU")]
    public void Works_out_the_country_of_a_location(string state, string currency, string country)
    {
        var company = new Company { CompanyId = "X", Name = "X", Ticker = "X", Exchange = "X", Sector = "X", Currency = currency };
        var location = new CompanyLocation { LocationId = "X", CompanyId = "X", Type = LocationType.Headquarters, Label = "HQ", City = "Town", State = state, Point = new GeoPoint(0, 0) };
        Assert.Equal(country, TopPaidCeoService.CountryOf(location, company));
    }

    [Fact]
    public async Task Unknown_place_is_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => Search(new() { Near = "00000" }));

    [Fact]
    public void Validator_reports_missing_location_and_bad_radius()
    {
        var errors = new GetCompaniesNearValidator(Opt.Monitor(new SearchOptions()))
            .Validate(new GetCompaniesNearQuery(new() { RadiusMiles = 5000 })).ToList();
        Assert.Contains(errors, e => e.Field == "near");
        Assert.Contains(errors, e => e.Field == "radiusMiles");
    }
}

public class ServiceRequestorPipelineTests
{
    private sealed class TestHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record Ping(string Name) : IRequest<string>, ICacheableRequest
    {
        public string CacheKey => Name;
        public string CacheProfile => "Test";
    }

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public static int Calls;
        public Task<string> HandleAsync(Ping request, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.FromResult("pong " + request.Name); }
    }

    private sealed class PingValidator : IRequestValidator<Ping>
    {
        public IEnumerable<ValidationError> Validate(Ping request)
        {
            if (request.Name.Length == 0) yield return new("name", "required");
        }
    }

    private static IServiceRequestor Build(bool cache)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Enabled"] = cache.ToString(),
            ["Caching:Profiles:Test"] = "60",
            ["Geo:ZipTablePath"] = "unused.csv"
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IHostEnvironment, TestHost>()
            .AddCompanyPaisaCore(config)
            .AddCompanyPaisaInfrastructure(config)
            .AddTransient<IRequestHandler<Ping, string>, PingHandler>()
            .AddTransient<IRequestValidator<Ping>, PingValidator>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IServiceRequestor>();
    }

    [Fact]
    public async Task Sends_request_to_its_handler() =>
        Assert.Equal("pong a", await Build(cache: false).SendAsync(new Ping("a")));

    [Fact]
    public async Task Validation_runs_before_the_handler() =>
        await Assert.ThrowsAsync<RequestValidationException>(() => Build(cache: false).SendAsync(new Ping("")));

    [Fact]
    public async Task Cacheable_requests_hit_the_handler_once()
    {
        var requestor = Build(cache: true);
        var before = PingHandler.Calls;
        await requestor.SendAsync(new Ping("cached-" + Guid.NewGuid()));
        var name = "same-" + Guid.NewGuid();
        await requestor.SendAsync(new Ping(name));
        await requestor.SendAsync(new Ping(name));
        Assert.Equal(before + 2, PingHandler.Calls);
    }
}
