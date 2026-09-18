using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Features.Executives;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class ExecutiveTests
{
    private static readonly GeoPoint Lehi = new(40.3916, -111.8508);

    // LOCAL (Lehi) and FAR (Salt Lake City, ~25 mi). Person P2 moved from FAR to LOCAL in 2021.
    private static FakeRepository Repo() => new FakeRepository()
        .Add("LOCAL", "Software", LocationType.Headquarters, 40.4000, -111.8600, 500)
        .Add("OTHER", "Finance", LocationType.Office, 40.4300, -111.8900, 900)
        .Add("FAR", "Software", LocationType.Headquarters, 40.7600, -111.8900, 2000)
        .Pay("P1", "LOCAL", "Chief Executive Officer", 2016, 2020, 3_000_000)           // left LOCAL for FAR in 2021
        .Pay("P1", "FAR", "Chief Executive Officer", 2021, 2025, 6_000_000)
        .Pay("P2", "FAR", "Chief Financial Officer", 2016, 2020, 1_000_000)             // came to LOCAL in 2021
        .Pay("P2", "LOCAL", "Chief Executive Officer", 2021, 2025, 4_000_000)
        .Pay("P3", "OTHER", "Chief Financial Officer", 2016, 2025, 2_000_000)
        .Pay("P4", "FAR", "Chief Operating Officer", 2016, 2025, 9_000_000);            // never local

    private static Task<ExecutivesNearResponse> Search(ExecutivesNearRequest r)
    {
        var repo = Repo();
        var handler = new GetExecutivesNearHandler(repo,
            new NearbySearchService(repo, new FakeGeoLocator(("84043", Lehi)), new HaversineDistanceCalculator()),
            new CurrencyConverter(Opt.Monitor(new CurrencyOptions())),
            Opt.Monitor(new SearchOptions()), Opt.Monitor(new MetricsOptions()));
        return handler.HandleAsync(new GetExecutivesNearQuery(r), CancellationToken.None);
    }

    [Fact]
    public async Task Lists_current_executives_of_nearby_companies_by_latest_pay()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10 });

        Assert.Equal(["P2", "P3"], result.Items.Select(e => e.PersonId));   // P1 left, P4 was never local
        var p2 = result.Items[0];
        Assert.Equal("LOCAL", p2.Company.Ticker);
        Assert.Equal(4_000_000, p2.LatestTotalPay);
        Assert.Equal(2025, p2.LatestYear);
        Assert.Equal(2, p2.CompanyCount);                                   // FAR then LOCAL
        Assert.Equal(5 * 1_000_000 + 5 * 4_000_000, p2.WindowTotalPay);     // earnings at both companies
        Assert.Equal(10, p2.PayHistory.Count);
        Assert.Equal("FAR", p2.PayHistory[0].Ticker);
    }

    [Fact]
    public async Task Recent_appointments_are_tagged_or_listed_with_their_announced_package()
    {
        var repo = Repo();
        var announced = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
        NewExecutive Hire(string? personId, string name, string title, decimal salary, decimal stock, DateOnly? on = null) => new()
        {
            CompanyId = "LOCAL", PersonId = personId, Name = name, Title = title, AnnouncedOn = on ?? announced,
            Package = [new(PackageItemKind.Salary, salary, "Base salary"), new(PackageItemKind.Stock, stock, "Stock awards")]
        };
        repo.Appointments.Add(Hire(null, "Nora New", "Chief Financial Officer", 500_000, 2_000_000));                 // no pay reported yet
        repo.Appointments.Add(Hire("P2", "Person P2", "President and Chief Executive Officer", 900_000, 5_000_000));   // already listed
        repo.Appointments.Add(Hire(null, "Olga Old", "Chief Operating Officer", 400_000, 1_000_000, announced.AddYears(-2)));   // too long ago
        var handler = new GetExecutivesNearHandler(repo,
            new NearbySearchService(repo, new FakeGeoLocator(("84043", Lehi)), new HaversineDistanceCalculator()),
            new CurrencyConverter(Opt.Monitor(new CurrencyOptions())), Opt.Monitor(new SearchOptions()), Opt.Monitor(new MetricsOptions()));

        var result = await handler.HandleAsync(new GetExecutivesNearQuery(new() { Near = "84043", RadiusMiles = 10 }), CancellationToken.None);
        var cfos = await handler.HandleAsync(new GetExecutivesNearQuery(new() { Near = "84043", RadiusMiles = 10, Role = ExecutiveRole.Cfo }), CancellationToken.None);

        var nora = Assert.Single(result.Items, e => e.Name == "Nora New");
        Assert.False(nora.HasProfile);
        Assert.Equal(2_500_000, nora.NewHire!.Total);
        Assert.Empty(nora.PayHistory);
        var p2 = Assert.Single(result.Items, e => e.PersonId == "P2");
        Assert.Equal("President and Chief Executive Officer", p2.Title);
        Assert.Equal(4_000_000, p2.LatestTotalPay);                        // still their reported pay
        Assert.NotNull(p2.NewHire?.PersonId);                              // has a page to link to
        Assert.DoesNotContain(result.Items, e => e.Name == "Olga Old");
        Assert.Equal(4_000_000 + 2_000_000, result.Summary.CombinedLatestPay);   // announced packages aren't pay received
        Assert.Contains(cfos.Items, e => e.Name == "Nora New");
    }

    [Fact]
    public async Task Role_narrows_the_list_to_people_whose_title_holds_it()
    {
        var ceos = await Search(new() { Near = "84043", RadiusMiles = 10, Role = ExecutiveRole.Ceo });
        var cfos = await Search(new() { Near = "84043", RadiusMiles = 10, Role = ExecutiveRole.Cfo });
        var others = await Search(new() { Near = "84043", RadiusMiles = 10, Role = ExecutiveRole.Other });

        Assert.Equal(["P2"], ceos.Items.Select(e => e.PersonId));
        Assert.Equal(1, ceos.TotalCount);
        Assert.Equal(["P3"], cfos.Items.Select(e => e.PersonId));
        Assert.Empty(others.Items);
    }

    [Theory]
    [InlineData("President and Chief Executive Officer", ExecutiveRole.Ceo, true)]
    [InlineData("Chairman & CEO", ExecutiveRole.Ceo, true)]
    [InlineData("Former Chief Executive Officer", ExecutiveRole.Ceo, false)]
    [InlineData("Executive Vice President and Chief Financial Officer", ExecutiveRole.Cfo, true)]   // "vice" is fine outside the CEO rule
    [InlineData("Chief Operating Officer and Chief Financial Officer", ExecutiveRole.Coo, true)]
    [InlineData("Chief Operating Officer and Chief Financial Officer", ExecutiveRole.Cfo, true)]
    [InlineData("Deputy Chief Financial Officer", ExecutiveRole.Cfo, false)]
    [InlineData("EVP, CTO", ExecutiveRole.Technology, true)]
    [InlineData("Chief Information Officer", ExecutiveRole.Technology, true)]
    [InlineData("Chief Legal Officer and Corporate Secretary", ExecutiveRole.Legal, true)]
    [InlineData("General Counsel and Secretary", ExecutiveRole.Legal, true)]
    [InlineData("M.D. Chief Medical Officer", ExecutiveRole.Other, true)]
    [InlineData("Chief Revenue Officer", ExecutiveRole.Other, true)]
    [InlineData("Former Chief Financial Officer", ExecutiveRole.Other, false)]   // in neither CFO nor Other
    public void Titles_hold_roles(string title, ExecutiveRole role, bool expected) =>
        Assert.Equal(expected, ExecutiveRoles.Holds(title, role));

    [Fact]
    public async Task Include_former_adds_people_who_moved_away()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, IncludeFormer = true });
        var p1 = Assert.Single(result.Items, e => e.PersonId == "P1");
        Assert.False(p1.IsCurrent);
        Assert.Equal("LOCAL", p1.Company.Ticker);                           // shown at the nearby company they left
        Assert.Equal(6_000_000, p1.LatestTotalPay);                         // but their latest pay is from FAR
    }

    [Fact]
    public async Task Years_limits_the_totals_window()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, Years = 3 });
        var p2 = result.Items.Single(e => e.PersonId == "P2");
        Assert.Equal(3 * 4_000_000, p2.WindowTotalPay);
        Assert.Equal(1, p2.CompanyCount);
    }

    [Fact]
    public async Task Search_matches_name_or_title()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10, Search = "financial" });
        Assert.Equal(["P3"], result.Items.Select(e => e.PersonId));
    }

    [Fact]
    public async Task Summary_counts_people_companies_and_median_pay()
    {
        var result = await Search(new() { Near = "84043", RadiusMiles = 10 });
        Assert.Equal(2, result.Summary.ExecutiveCount);
        Assert.Equal(6_000_000, result.Summary.CombinedLatestPay);
        Assert.Equal(3_000_000, result.Summary.MedianLatestPay);
    }

    [Fact]
    public async Task Career_shows_every_company_and_total_earnings()
    {
        var detail = await new GetExecutiveHandler(Repo()).HandleAsync(new GetExecutiveQuery("P2"), CancellationToken.None);

        Assert.Equal("LOCAL", detail.CurrentCompany.Ticker);
        Assert.Equal(2016, detail.FirstYear);
        Assert.Equal(25_000_000, detail.TotalPay);
        Assert.Collection(detail.Roles,
            r => { Assert.Equal("LOCAL", r.Company.Ticker); Assert.Equal((2021, 2025), (r.FromYear, r.ToYear)); },
            r => { Assert.Equal("FAR", r.Company.Ticker); Assert.Equal("Chief Financial Officer", r.Title); });
        Assert.Equal(10, detail.History.Count);
    }

    [Fact]
    public async Task Unknown_person_is_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => new GetExecutiveHandler(Repo()).HandleAsync(new GetExecutiveQuery("NOPE"), CancellationToken.None));
}
