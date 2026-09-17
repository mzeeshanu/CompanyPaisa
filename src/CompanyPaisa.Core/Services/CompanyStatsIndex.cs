using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>One company with its headquarters and headline indicators, for comparisons across the whole data set.</summary>
public sealed record CompanyStats(Company Company, CompanyLocation? Headquarters, CompanyIndicators Indicators, decimal TtmRevenueUsd);

public sealed record CompanyStatsSet(IReadOnlyList<CompanyStats> All, IReadOnlyDictionary<string, CompanyStats> ById);

/// <summary>Every company's headline figures, worked out once per data set (ranks and sector medians need all of them).</summary>
public interface ICompanyStatsIndex
{
    Task<CompanyStatsSet> GetAsync(CancellationToken ct = default);
}

/// <summary>Built on first use and rebuilt when the repository hands out a new company list (a data reload).</summary>
public sealed class CompanyStatsIndex(ICompanyRepository repository, IFinancialMetricsService metrics, ICurrencyConverter fx) : ICompanyStatsIndex
{
    private readonly SemaphoreSlim _build = new(1, 1);
    private volatile Built? _built;

    private sealed record Built(IReadOnlyList<Company> Source, CompanyStatsSet Set);

    public async Task<CompanyStatsSet> GetAsync(CancellationToken ct = default)
    {
        var source = await repository.GetCompaniesAsync(ct);
        if (_built is { } b && ReferenceEquals(b.Source, source)) return b.Set;
        await _build.WaitAsync(ct);
        try
        {
            if (_built is { } again && ReferenceEquals(again.Source, source)) return again.Set;
            var financials = await repository.GetFinancialsAsync(source.Select(c => c.CompanyId), ct);
            var all = new List<CompanyStats>(source.Count);
            foreach (var c in source)
            {
                var locations = await repository.GetLocationsAsync(c.CompanyId, ct);
                var indicators = metrics.Compute(financials.GetValueOrDefault(c.CompanyId) ?? []);
                all.Add(new CompanyStats(c, locations.FirstOrDefault(l => l.IsHeadquarters) ?? locations.FirstOrDefault(), indicators,
                    fx.ToUsd(indicators.TtmRevenue, c.Currency)));
            }
            var set = new CompanyStatsSet(all, all.GroupBy(s => s.Company.CompanyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));
            _built = new Built(source, set);
            return set;
        }
        finally { _build.Release(); }
    }
}
