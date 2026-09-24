using CompanyPaisa.Client;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Api.Tests;

/// <summary>
/// How the website loads a big area: every company as a small bubble once, and the ranked list a page at a time,
/// sorted, reversed and searched by the API.
/// </summary>
public class NearbyPagingApiTests(SampleDataFactory factory) : IClassFixture<SampleDataFactory>
{
    private CompanyPaisaClient Client() => new(factory.CreateClient());

    private Task<NearbyCompaniesResponse> Near(bool bubbles = false, int page = 1, int pageSize = 5, bool reverse = false,
        string? search = null, CompanySort sort = CompanySort.Revenue) =>
        Client().GetCompaniesNearAsync(new NearbyCompaniesRequest
        {
            Near = "84043", RadiusMiles = 50, IncludeBubbles = bubbles, Page = page, PageSize = pageSize, Reverse = reverse, Search = search, Sort = sort,
        });

    [Fact]
    public async Task Bubbles_cover_every_company_while_items_are_one_page()
    {
        var first = await Near(bubbles: true);
        var all = await Near(pageSize: 5000);

        Assert.Equal(5, first.Items.Count);
        Assert.Equal(all.TotalCount, first.Bubbles!.Count);
        Assert.Equal(all.Items.Select(c => c.Ticker).Order(), first.Bubbles.Select(b => b.Ticker).Order());
        var lfvn = Assert.Single(first.Bubbles, b => b.Ticker == "LFVN");
        Assert.True(lfvn.IsHeadquarteredNearby);
        Assert.Null((await Near()).Bubbles);                                  // only when asked for
    }

    [Fact]
    public async Task Pages_follow_on_without_gaps_or_repeats()
    {
        var all = (await Near(pageSize: 5000)).Items.Select(c => c.Ticker).ToList();
        var paged = new List<string>();
        for (var page = 1; paged.Count < all.Count; page++) paged.AddRange((await Near(page: page)).Items.Select(c => c.Ticker));
        Assert.Equal(all, paged);
    }

    [Fact]
    public async Task Reverse_turns_the_order_around_but_keeps_missing_growth_last()
    {
        var biggest = (await Near(pageSize: 5000)).Items;
        var smallest = (await Near(pageSize: 5000, reverse: true)).Items;
        Assert.Equal(biggest.Select(c => c.Indicators.TtmRevenue).Order(), smallest.Select(c => c.Indicators.TtmRevenue));

        var growth = (await Near(pageSize: 5000, reverse: true, sort: CompanySort.Growth)).Items;
        var known = growth.TakeWhile(c => c.Indicators.RevenueGrowthYoY is not null).ToList();
        Assert.All(growth.Skip(known.Count), c => Assert.Null(c.Indicators.RevenueGrowthYoY));
        Assert.Equal(known.Select(c => c.Indicators.RevenueGrowthYoY).Order(), known.Select(c => c.Indicators.RevenueGrowthYoY));
    }

    [Fact]
    public async Task Search_narrows_the_list_but_not_the_summary_or_bubbles()
    {
        var whole = await Near(bubbles: true, pageSize: 5000);
        var found = await Near(bubbles: true, search: "lifevant");
        var byTicker = await Near(search: "lfv");

        Assert.Equal("LFVN", Assert.Single(found.Items).Ticker);
        Assert.Equal(1, found.TotalCount);
        Assert.Equal("LFVN", Assert.Single(byTicker.Items).Ticker);
        Assert.Equal(whole.Summary.CompanyCount, found.Summary.CompanyCount);
        Assert.Equal(whole.Bubbles!.Count, found.Bubbles!.Count);
    }
}
