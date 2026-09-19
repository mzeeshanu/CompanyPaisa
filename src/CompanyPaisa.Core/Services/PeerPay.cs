using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// How a company's executive pay compares with similar companies: the same sector, a similar size and the same kind of filing
/// (UK annual reports list executive directors' "single total figure", US proxies list named executive officers, Pakistani
/// annual reports the chief executive's remuneration — so they aren't mixed). Compares the top executive (the CEO, or
/// the top-paid executive director) and the typical other executive.
/// </summary>
public static class PeerPay
{
    /// <summary>"Similar size": revenue between a third of this company's and three times it, when that gives enough peers.</summary>
    public const decimal SizeBand = 3m;
    /// <summary>Otherwise (the biggest and smallest companies of a sector) the companies closest in size, this many of them.</summary>
    public const int ClosestInSize = 20;
    /// <summary>The fewest peers worth comparing against.</summary>
    public const int MinPeers = 5;
    /// <summary>How many of the peers closest in size are listed next to this company, ranked by pay.</summary>
    public const int Listed = 8;

    private sealed record CompanyPay(Company Company, CompanyStats Stats, int Year, string Role, decimal TopPay, decimal? OtherMedian, string Currency);

    public static async Task<PayVsPeersDto?> CompareAsync(Company company, CompanyStatsSet set, string noSector,
        ICompanyRepository repository, ICurrencyConverter fx, CancellationToken ct)
    {
        if (string.Equals(company.Sector, noSector, StringComparison.OrdinalIgnoreCase)) return null;
        var me = set.ById[company.CompanyId];
        var market = PayMarket(company);
        var sector = set.All.Where(s => string.Equals(s.Company.Sector, company.Sector, StringComparison.OrdinalIgnoreCase)
            && PayMarket(s.Company) == market && s.TtmRevenueUsd > 0).ToList();

        var rows = await repository.GetExecutiveCompensationAsync(sector.Select(s => s.Company.CompanyId), ct);
        var pay = rows.GroupBy(r => r.CompanyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => Summarise(set.ById[g.Key], g.ToList()))
            .OfType<CompanyPay>()
            .ToDictionary(p => p.Company.CompanyId, StringComparer.OrdinalIgnoreCase);
        if (!pay.TryGetValue(company.CompanyId, out var mine) || me.TtmRevenueUsd <= 0) return null;

        // Recent pay only: a company whose latest report is years older than this one's isn't a fair comparison.
        var recent = pay.Values.Where(p => p != mine && p.Year >= mine.Year - 1).ToList();
        var peers = recent.Where(p => p.Stats.TtmRevenueUsd >= me.TtmRevenueUsd / SizeBand && p.Stats.TtmRevenueUsd <= me.TtmRevenueUsd * SizeBand).ToList();
        if (peers.Count < ClosestInSize)
            peers = recent.OrderBy(p => SizeGap(p, me)).Take(ClosestInSize).ToList();
        if (peers.Count < MinPeers) return null;

        var currency = mine.Currency;
        decimal In(CompanyPay p, decimal amount) => fx.Convert(amount, p.Currency, currency);
        var tops = peers.Select(p => In(p, p.TopPay)).Order().ToList();
        var others = peers.Where(p => p.OtherMedian is not null).Select(p => In(p, p.OtherMedian!.Value)).Order().ToList();
        var compareOthers = mine.OtherMedian is not null && others.Count >= MinPeers;

        // The most similar companies (closest in size) and this one, highest pay first: who pays more, who pays less.
        var nearby = peers.OrderBy(p => SizeGap(p, me)).Take(Listed).Append(mine)
            .OrderByDescending(p => In(p, p.TopPay))
            .Select(p => new PeerPayDto(p.Company.Ticker, p.Company.Name, p.Role, Math.Round(In(p, p.TopPay)), p.Year, p == mine)).ToList();

        // The size range the peers actually span, in this company's reporting currency.
        decimal Revenue(CompanyPay p) => fx.Convert(p.Stats.Indicators.TtmRevenue, p.Company.Currency, company.Currency);
        return new PayVsPeersDto(
            company.Sector,
            Math.Round(peers.Min(Revenue)), Math.Round(peers.Max(Revenue)),
            peers.Count, currency,
            peers.Any(p => !string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase)),
            mine.Role, mine.TopPay, Math.Round(Median(tops)), Percentile(tops, mine.TopPay),
            compareOthers ? mine.OtherMedian : null,
            compareOthers ? Math.Round(Median(others)) : null,
            compareOthers ? Percentile(others, mine.OtherMedian!.Value) : null,
            nearby);
    }

    /// <summary>The latest reported year: the top executive (CEO, else the top-paid executive director) and the median of the others.</summary>
    private static CompanyPay? Summarise(CompanyStats stats, List<ExecutiveCompensation> rows)
    {
        var year = rows.Where(r => r.Year <= DateTime.UtcNow.Year && r.Total > 0).Select(r => r.Year).DefaultIfEmpty().Max();
        if (year == 0) return null;
        var latest = rows.Where(r => r.Year == year && r.Total > 0).ToList();
        var top = latest.Where(r => TopPaidCeoService.RoleOf(r.Title) is not null).MaxBy(r => r.Total);
        if (top is null) return null;
        var others = latest.Where(r => !string.Equals(r.PersonId, top.PersonId, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Total).Order().ToList();
        var role = TopPaidCeoService.RoleOf(top.Title) == "CEO" ? "CEO" : "top-paid executive director";
        return new CompanyPay(stats.Company, stats, year, role, top.Total, others.Count >= 2 ? Median(others) : null,
            stats.Company.PayCurrency ?? stats.Company.Currency);
    }

    /// <summary>How far apart two companies' revenues are, as a ratio (so $100M vs $200M is as far as $1B vs $2B).</summary>
    private static double SizeGap(CompanyPay p, CompanyStats me) => Math.Abs(Math.Log((double)(p.Stats.TtmRevenueUsd / me.TtmRevenueUsd)));

    /// <summary>Which kind of pay disclosure a company files: UK annual reports (".L"), Pakistani annual reports (".KA"), else SEC proxies.</summary>
    private static string PayMarket(Company c) =>
        c.Ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase) ? "uk" : c.Ticker.EndsWith(".KA", StringComparison.OrdinalIgnoreCase) ? "pk" : "sec";

    private static decimal Median(IReadOnlyList<decimal> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    /// <summary>Share of the peers paid less, 0–100.</summary>
    private static int Percentile(IReadOnlyList<decimal> peers, decimal value) =>
        (int)Math.Round(100m * peers.Count(p => p < value) / peers.Count);
}
