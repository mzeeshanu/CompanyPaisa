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
