using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Features.Executives;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Services;

namespace CompanyPaisa.Core.Features.Companies;

// ---------- Company insights: "at a glance" facts and similar companies nearby ----------

/// <remarks>Not recorded in the analytics: it loads with the company page, whose view is already recorded.</remarks>
public sealed record GetCompanyInsightsQuery(string Ticker, bool IncludeExecutives) : IRequest<CompanyInsightsResponse>, ICacheableRequest
{
    public string CacheKey => $"insights|{Ticker.ToUpperInvariant()}|{IncludeExecutives}";
    public string CacheProfile => "Company";
}

public sealed class GetCompanyInsightsHandler(
    ICompanyRepository repository,
    ICompanyStatsIndex statsIndex,
    IFinancialMetricsService metrics,
    ITopPaidCeoService topCeo,
    IDistanceCalculator distance) : IRequestHandler<GetCompanyInsightsQuery, CompanyInsightsResponse>
{
    /// <summary>Fewest companies a rank or a sector median is worth quoting against.</summary>
    public const int MinRankCount = 3, MinSectorForMedian = 5;
    /// <summary>A run of growth or decline must be at least this long to mention.</summary>
    public const int MinStreak = 3;
    /// <summary>"Similar" = same sector within this many miles of the headquarters; otherwise any company within the nearby radius.</summary>
    public const double SameSectorMiles = 100, AnySectorMiles = 25;
    public const int SimilarCount = 5;
    private const decimal SecondsPerYear = 31_557_600m;
    /// <summary>Companies without a real sector (European filers) are grouped under this name; ranking within it means nothing.</summary>
    public const string NoSector = "Other";

    public async Task<CompanyInsightsResponse> HandleAsync(GetCompanyInsightsQuery q, CancellationToken ct)
    {
        var company = await repository.GetCompanyAsync(q.Ticker, ct) ?? throw GetCompanyHandler.CompanyNotFound(q.Ticker);
        var set = await statsIndex.GetAsync(ct);
        var me = set.ById[company.CompanyId];
        var ind = me.Indicators;
        var periods = await repository.GetFinancialsAsync(company.CompanyId, ct);

        var hasSector = !string.Equals(company.Sector, NoSector, StringComparison.OrdinalIgnoreCase);
        var sector = !hasSector ? [] : set.All.Where(s => string.Equals(s.Company.Sector, company.Sector, StringComparison.OrdinalIgnoreCase)).ToList();
        var hq = me.Headquarters;
        var sameCity = hq is null ? [] : set.All.Where(s => s.Headquarters is { } h
            && string.Equals(h.City, hq.City, StringComparison.OrdinalIgnoreCase) && string.Equals(h.State, hq.State, StringComparison.OrdinalIgnoreCase)).ToList();

        TopPaidCeoDto? ceo = q.IncludeExecutives ? await topCeo.FindAsync([company], ct) : null;
        var (similar, sameSector) = Similar(me, set, hasSector);

        // Officers appointed recently, with the package the company announced (newest first).
        var appointments = q.IncludeExecutives
            ? (await repository.GetNewExecutivesAsync([company.CompanyId], ct)).OrderByDescending(e => e.AnnouncedOn).ToList()
            : [];
        var profiles = await NewExecutives.WithProfilesAsync(repository, appointments, ct);
        var newExecutives = appointments
            .Select(e => NewExecutives.ToDto(e, company, e.PersonId is not null && profiles.Contains(e.PersonId))).ToList();

        return new CompanyInsightsResponse(
            company.Ticker, company.Currency,
            ceo is null ? null : await PayVsResultsAsync(company, ceo, ct),
            Rank(me, sector, company.Sector),
            hq is null ? null : Rank(me, sameCity, $"{hq.City}, {hq.State}"),
            Streak(periods),
            Records(ind),
            ceo?.MedianWorker is null ? null : ceo,
            MarginVsSector(me, sector, company.Sector),
            company.Employees is > 0 && ind.TtmRevenue > 0 ? Math.Round(ind.TtmRevenue / company.Employees.Value) : null,
            company.Employees is > 0 ? company.Employees : null,
            Math.Round(Math.Max(0, ind.TtmRevenue) / SecondsPerYear, 2),
            sameSector, similar, newExecutives);
    }

    /// <summary>The CEO's pay change in their latest year next to the revenue change of the fiscal year with the same number.</summary>
    private async Task<PayVsResultsDto?> PayVsResultsAsync(Company company, TopPaidCeoDto ceo, CancellationToken ct)
    {
        if (ceo.Role != "CEO") return null;
        var prior = (await repository.GetExecutiveCompensationAsync(company.CompanyId, ct))
            .Where(r => string.Equals(r.PersonId, ceo.PersonId, StringComparison.OrdinalIgnoreCase) && r.Year == ceo.Year - 1)
            .Sum(r => r.Total);
        var years = (await repository.GetFinancialsAsync(company.CompanyId, ct)).Where(p => p.PeriodType == PeriodType.Annual).ToList();
        var thisYear = years.FirstOrDefault(p => p.FiscalYear == ceo.Year);
        var lastYear = years.FirstOrDefault(p => p.FiscalYear == ceo.Year - 1);
        if (prior <= 0 || thisYear is null || lastYear is not { Revenue: > 0 }) return null;
        return new PayVsResultsDto(ceo.PersonId, ceo.Name, ceo.Title, ceo.Year, ceo.TotalPay, ceo.Currency,
            Math.Round(ceo.TotalPay / prior - 1, 4), Math.Round(thisYear.Revenue / lastYear.Revenue - 1, 4));
    }

    private static RankDto? Rank(CompanyStats me, IReadOnlyList<CompanyStats> group, string within)
    {
        var ranked = group.Where(s => s.TtmRevenueUsd > 0).OrderByDescending(s => s.TtmRevenueUsd).ToList();
        var at = ranked.FindIndex(s => ReferenceEquals(s, me));
        return at < 0 || ranked.Count < MinRankCount ? null : new RankDto(at + 1, ranked.Count, within);
    }

    private RevenueStreakDto? Streak(IReadOnlyList<FinancialPeriod> periods)
    {
        foreach (var unit in new[] { PeriodType.Quarterly, PeriodType.Annual })
        {
            var growth = metrics.WithGrowth(periods, unit);
            if (growth.Count == 0 || growth[^1].RevenueGrowthYoY is not { } last || last == 0) continue;
            var up = last > 0;
            var count = 0;
            for (var i = growth.Count - 1; i >= 0 && growth[i].RevenueGrowthYoY is { } g && g != 0 && g > 0 == up; i--) count++;
            if (count >= MinStreak) return new RevenueStreakDto(up ? TrendStatus.Up : TrendStatus.Down, count, unit);
            if (unit == PeriodType.Quarterly) return null;   // a company with quarters: a short quarterly run says enough
        }
        return null;
    }

    private static RecordsDto? Records(CompanyIndicators ind)
    {
        var years = ind.AnnualHistory;
        if (years.Count < MinRankCount) return null;
        var best = years.MaxBy(y => y.Revenue)!;
        return new RecordsDto(best.FiscalYear, best.Revenue, best.FiscalYear == years[^1].FiscalYear,
            years.Count(y => y.NetIncome > 0), years.Count);
    }

    private static MarginComparisonDto? MarginVsSector(CompanyStats me, IReadOnlyList<CompanyStats> sector, string name)
    {
        if (me.Indicators.NetMargin is not { } margin || me.Indicators.TtmRevenue <= 0) return null;
        var margins = sector.Where(s => s.Indicators.TtmRevenue > 0 && s.Indicators.NetMargin is not null)
            .Select(s => s.Indicators.NetMargin!.Value).Order().ToList();
        if (margins.Count < MinSectorForMedian) return null;
        var mid = margins.Count / 2;
        var median = margins.Count % 2 == 1 ? margins[mid] : (margins[mid - 1] + margins[mid]) / 2;
        return new MarginComparisonDto(margin, Math.Round(median, 4), margins.Count, name);
    }

    /// <summary>
    /// The nearest companies in the same sector (when it has one); when there are hardly any, the nearest companies of any kind.
    /// Headquarters often share a postcode's centre, so distances are compared in whole miles and bigger companies win ties.
    /// </summary>
    private (IReadOnlyList<SimilarCompanyDto>, bool SameSector) Similar(CompanyStats me, CompanyStatsSet set, bool hasSector)
    {
        if (me.Headquarters is not { } hq) return ([], true);
        var near = set.All
            .Where(s => !ReferenceEquals(s, me) && s.Headquarters is not null && s.Indicators.TtmRevenue > 0)
            .Select(s => (Stats: s, Miles: distance.DistanceMiles(hq.Point, s.Headquarters!.Point)))
            .ToList();
        var same = !hasSector ? [] : near
            .Where(x => x.Miles <= SameSectorMiles && string.Equals(x.Stats.Company.Sector, me.Company.Sector, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => Math.Round(x.Miles)).ThenByDescending(x => x.Stats.TtmRevenueUsd).Take(SimilarCount).ToList();
        var sameSector = same.Count >= 2;
        var picked = sameSector ? same : near.Where(x => x.Miles <= AnySectorMiles)
            .OrderBy(x => Math.Round(x.Miles)).ThenByDescending(x => x.Stats.TtmRevenueUsd).Take(SimilarCount).ToList();
        return (picked.Select(x => new SimilarCompanyDto(x.Stats.Company.Ticker, x.Stats.Company.Name, x.Stats.Company.Sector,
            x.Stats.Headquarters!.City, x.Stats.Headquarters.State, Math.Round(x.Miles, 1), x.Stats.Indicators.TtmRevenue,
            x.Stats.Indicators.RevenueGrowthYoY, x.Stats.Indicators.Trend, x.Stats.Company.Currency)).ToList(), sameSector);
    }
}
