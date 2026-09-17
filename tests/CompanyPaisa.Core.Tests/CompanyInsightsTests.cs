using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Features.Companies;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Tests;

public class CompanyInsightsTests
{
    /// <summary>Five software companies around Lehi (sizes in dollars of yearly revenue), a bank next door, and one in Denver.</summary>
    private static FakeRepository Repo()
    {
        var repo = new FakeRepository()
            .Add("BIG", "Software", LocationType.Headquarters, 40.39, -111.85, 1_000_000_000)
            .Add("MID", "Software", LocationType.Headquarters, 40.39, -111.85, 500_000_000, growth: -0.10m)
            .Add("SMALL", "Software", LocationType.Headquarters, 40.52, -111.87, 100_000_000, margin: -0.2m)
            .Add("TINY", "Software", LocationType.Headquarters, 40.45, -111.90, 20_000_000)
            .Add("WEE", "Software", LocationType.Headquarters, 40.30, -111.70, 10_000_000)
            .Add("BANK", "Banking", LocationType.Headquarters, 40.39, -111.85, 2_000_000_000)
            .Add("DENV", "Software", LocationType.Headquarters, 39.74, -104.99, 5_000_000_000);
        // Ten years of annual results for MID: best year 2019, losses in 2020 and 2021.
        for (var y = 2016; y <= 2025; y++)
            repo.Financials.Add(new FinancialPeriod
            {
                CompanyId = "MID", PeriodType = PeriodType.Annual, FiscalYear = y,
                Revenue = y == 2019 ? 900_000_000 : 400_000_000 + (y - 2016) * 10_000_000, NetIncome = y is 2020 or 2021 ? -1 : 5_000_000
            });
        repo.Pay("ceo-1", "MID", "President and Chief Executive Officer", 2024, 2024, 2_000_000)
            .Pay("ceo-1", "MID", "President and Chief Executive Officer", 2025, 2025, 3_000_000)
            .Pay("cfo-1", "MID", "Chief Financial Officer", 2025, 2025, 9_000_000);
        return repo;
    }

    private static Task<CompanyInsightsResponse> Insights(FakeRepository repo, string ticker, bool executives = true)
    {
        var metrics = new FinancialMetricsService(Opt.Monitor(new MetricsOptions()));
        var fx = new CurrencyConverter(Opt.Monitor(new CurrencyOptions()));
        var ceo = new TopPaidCeoService(repo, fx, Opt.Monitor(new BenchmarkOptions
        {
            MedianPay = { ["US"] = new MedianPayOptions { Description = "US full-time worker", AnnualPay = 65_000, Currency = "USD", Period = "2026", Source = "BLS", SourceUrl = "https://www.bls.gov/" } }
        }));
        var handler = new GetCompanyInsightsHandler(repo, new CompanyStatsIndex(repo, metrics, fx), metrics, ceo, new HaversineDistanceCalculator());
        return handler.HandleAsync(new GetCompanyInsightsQuery(ticker, executives), CancellationToken.None);
    }

    [Fact]
    public async Task Ranks_against_the_sector_and_the_home_city()
    {
        var mid = await Insights(Repo(), "MID");

        Assert.Equal(new RankDto(3, 6, "Software"), mid.SectorRank);         // DENV and BIG are bigger
        Assert.Equal(new RankDto(4, 7, "Town, UT"), mid.CityRank);           // the fake data puts everyone in "Town"
    }

    [Fact]
    public async Task The_ceos_pay_change_is_set_against_the_revenue_change_of_the_same_year()
    {
        var mid = await Insights(Repo(), "MID");

        var p = Assert.IsType<PayVsResultsDto>(mid.PayVsResults);
        Assert.Equal("ceo-1", p.PersonId);                                   // the chief executive, not the better-paid CFO
        Assert.Equal(2025, p.Year);
        Assert.Equal(0.5m, p.PayChange);
        Assert.Equal(Math.Round(490m / 480m - 1, 4), p.RevenueChange);
        Assert.NotNull(mid.CeoVsWorker?.MedianWorker);
        Assert.Null((await Insights(Repo(), "MID", executives: false)).PayVsResults);
    }

    [Fact]
    public async Task Streaks_records_and_margins_come_from_the_companys_history()
    {
        var mid = await Insights(Repo(), "MID");
        var big = await Insights(Repo(), "BIG");
        var small = await Insights(Repo(), "SMALL");

        Assert.Equal(new RevenueStreakDto(TrendStatus.Down, 4, PeriodType.Quarterly), mid.RevenueStreak);
        Assert.Equal(new RevenueStreakDto(TrendStatus.Up, 4, PeriodType.Quarterly), big.RevenueStreak);
        Assert.Equal(new RecordsDto(2019, 900_000_000, false, 8, 10), mid.Records);
        Assert.Null(big.Records);                                            // quarters only: no yearly history
        Assert.Equal(-0.2m, small.MarginVsSector?.NetMargin);
        Assert.Equal(0.1m, small.MarginVsSector?.SectorMedian);
        Assert.Equal(6, small.MarginVsSector?.SectorCount);
    }

    [Fact]
    public async Task Similar_companies_are_the_nearest_in_the_same_sector_bigger_first_on_ties()
    {
        var mid = await Insights(Repo(), "MID");

        Assert.True(mid.SimilarSameSector);
        Assert.Equal(["BIG", "TINY", "SMALL", "WEE"], mid.Similar.Select(s => s.Ticker));   // Denver is too far; the bank isn't software
        Assert.Equal(0, mid.Similar[0].DistanceMiles);
    }

    [Fact]
    public async Task Companies_without_a_real_sector_get_no_sector_facts()
    {
        var repo = Repo();
        foreach (var i in Enumerable.Range(0, repo.Companies.Count))
            repo.Companies[i] = repo.Companies[i] with { Sector = GetCompanyInsightsHandler.NoSector };

        var mid = await Insights(repo, "MID");

        Assert.Null(mid.SectorRank);
        Assert.Null(mid.MarginVsSector);
        Assert.False(mid.SimilarSameSector);
        Assert.Contains(mid.Similar, s => s.Ticker == "BANK");               // nearby companies of any kind instead
    }
}
