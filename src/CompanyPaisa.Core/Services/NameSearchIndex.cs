using System.Globalization;
using System.Text;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Core.Services;

/// <summary>Finds companies (by name or ticker) and executives (by name) across the whole data set.</summary>
public interface INameSearchIndex
{
    Task<NameSearchResponse> SearchAsync(string query, int limit, bool includeExecutives, CancellationToken ct = default);
}

/// <summary>
/// An in-memory index built from the repository on first use and rebuilt when the data set is reloaded (the repository hands
/// out a new company list). Matching ignores case, accents and punctuation; every word typed must start a word of the name,
/// so "tim co" finds Tim Cook and "bank am" finds Bank of America.
/// </summary>
public sealed class NameSearchIndex(ICompanyRepository repository, IFinancialMetricsService metrics, ICurrencyConverter fx) : INameSearchIndex
{
    private readonly SemaphoreSlim _build = new(1, 1);
    private volatile Built? _built;

    private sealed record CompanyEntry(NameSearchCompanyDto Dto, string Ticker, string TickerBase, string Name, string[] Words, decimal WeightUsd);
    private sealed record PersonEntry(NameSearchExecutiveDto Dto, string Name, string[] Words, decimal WeightUsd);
    private sealed record Built(IReadOnlyList<Company> Source, IReadOnlyList<CompanyEntry> Companies, IReadOnlyList<PersonEntry> People);

    public async Task<NameSearchResponse> SearchAsync(string query, int limit, bool includeExecutives, CancellationToken ct = default)
    {
        var index = await IndexAsync(ct);
        var q = Normalize(query);
        if (q.Length == 0) return new NameSearchResponse(query.Trim(), [], []);
        var words = q.Split(' ');
        var upper = query.Trim().ToUpperInvariant();

        var companies = index.Companies
            .Select(c => (c, score: ScoreCompany(c, q, words, upper)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.c.WeightUsd).ThenBy(x => x.c.Name, StringComparer.Ordinal)
            .Take(limit).Select(x => x.c.Dto).ToList();

        var people = !includeExecutives ? [] : index.People
            .Select(p => (p, score: ScoreName(p.Name, p.Words, q, words)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.p.WeightUsd).ThenBy(x => x.p.Name, StringComparer.Ordinal)
            .Take(limit).Select(x => x.p.Dto).ToList();

        return new NameSearchResponse(query.Trim(), companies, people);
    }

    private static int ScoreCompany(CompanyEntry c, string q, string[] words, string upper)
    {
        if (upper == c.Ticker || upper == c.TickerBase) return 100;   // "NVDA", "BP" for BP.L
        var byName = ScoreName(c.Name, c.Words, q, words);
        if (byName >= 60) return byName;
        if (upper.Length >= 2 && c.Ticker.StartsWith(upper, StringComparison.Ordinal)) return 50;
        return byName;
    }

    /// <summary>Exact name 95, name starts with the text 80, every word typed starts a word of the name 60, the name contains it 30.</summary>
    private static int ScoreName(string name, string[] nameWords, string q, string[] words)
    {
        if (name == q) return 95;
        if (name.StartsWith(q, StringComparison.Ordinal)) return 80;
        if (words.All(w => nameWords.Any(n => n.StartsWith(w, StringComparison.Ordinal)))) return 60;
        return q.Length >= 3 && name.Contains(q, StringComparison.Ordinal) ? 30 : 0;
    }

    private async Task<Built> IndexAsync(CancellationToken ct)
    {
        var source = await repository.GetCompaniesAsync(ct);
        if (_built is { } b && ReferenceEquals(b.Source, source)) return b;
        await _build.WaitAsync(ct);
        try
        {
            if (_built is { } again && ReferenceEquals(again.Source, source)) return again;
            return _built = await BuildAsync(source, ct);
        }
        finally { _build.Release(); }
    }

    private async Task<Built> BuildAsync(IReadOnlyList<Company> source, CancellationToken ct)
    {
        var ids = source.Select(c => c.CompanyId).ToList();
        var financials = await repository.GetFinancialsAsync(ids, ct);
        var companies = new List<CompanyEntry>(source.Count);
        foreach (var c in source)
        {
            var hq = (await repository.GetLocationsAsync(c.CompanyId, ct)).OrderBy(l => l.IsHeadquarters ? 0 : 1).FirstOrDefault();
            var revenue = metrics.Compute(financials.GetValueOrDefault(c.CompanyId) ?? []).TtmRevenue;
            var name = Normalize(c.Name);
            var ticker = c.Ticker.ToUpperInvariant();
            companies.Add(new CompanyEntry(
                new NameSearchCompanyDto(c.Ticker, c.Name, c.Exchange, c.Sector, hq?.City, hq?.State, revenue, c.Currency),
                ticker, ticker.Split('.')[0], name, name.Split(' ', StringSplitOptions.RemoveEmptyEntries), fx.ToUsd(revenue, c.Currency)));
        }

        var byId = source.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var people = (await repository.GetExecutiveCompensationAsync(ids, ct))
            .GroupBy(p => p.PersonId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Where(p => byId.ContainsKey(p.CompanyId)).OrderByDescending(p => p.Year).ThenByDescending(p => p.Total).FirstOrDefault())
            .OfType<ExecutiveCompensation>()
            .Select(latest =>
            {
                var company = byId[latest.CompanyId];
                var currency = company.PayCurrency ?? company.Currency;
                var name = Normalize(latest.ExecutiveName);
                return new PersonEntry(
                    new NameSearchExecutiveDto(latest.PersonId, latest.ExecutiveName, latest.Title,
                        new CompanyRefDto(company.Ticker, company.Name, company.Sector, currency), latest.Year, latest.Total),
                    name, name.Split(' ', StringSplitOptions.RemoveEmptyEntries), fx.ToUsd(latest.Total, currency));
            })
            .ToList();

        return new Built(source, companies, people);
    }

    /// <summary>"Société Générale S.A." → "societe generale s a"; "AT&amp;T" → "at t".</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = true;
        foreach (var ch in text.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); space = false; }
            else if (!space && ch != '\'' && ch != '’') { sb.Append(' '); space = true; }
        }
        return sb.ToString().TrimEnd();
    }
}
