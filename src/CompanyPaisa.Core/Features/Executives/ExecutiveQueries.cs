using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Features.Search;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Features.Executives;

// ---------- Executives near me ----------

/// <summary>Named executive officers of public companies with a location within the radius, with their pay history.</summary>
public sealed record GetExecutivesNearQuery(ExecutivesNearRequest Request) : IRequest<ExecutivesNearResponse>, ICacheableRequest, ITrackedRequest<ExecutivesNearResponse>
{
    public string CacheKey => string.Create(CultureInfo.InvariantCulture,
        $"execnear|{Request.Near?.Trim().ToUpperInvariant()}|{Request.Latitude:F3}|{Request.Longitude:F3}|{Request.RadiusMiles}|{Request.Sector?.ToUpperInvariant()}|{Request.IncludeFormer}|{Request.Search?.Trim().ToUpperInvariant()}|{Request.Role}|{Request.Sort}|{Request.Years}|{Request.Page}|{Request.PageSize}");
    public string CacheProfile => "Search";

    /// <summary>The first page of a search only (later pages are "Show more" on the same search).</summary>
    public AnalyticsAction? Describe(ExecutivesNearResponse response) => (Request.Page ?? 1) != 1 ? null :
        SearchAnalytics.Action("executive_search", response.Origin, response.OriginLabel, response.RadiusMiles, Request.Near, Request.Sector,
            response.TotalCount, ("search", Request.Search?.Trim()), ("role", Request.Role?.ToString()), ("includeFormer", Request.IncludeFormer ? "true" : null));
}

public sealed class GetExecutivesNearValidator(IOptionsMonitor<SearchOptions> search, IOptionsMonitor<MetricsOptions> metrics)
    : IRequestValidator<GetExecutivesNearQuery>
{
    public IEnumerable<ValidationError> Validate(GetExecutivesNearQuery query)
    {
        var r = query.Request;
        foreach (var e in NearbyValidation.Validate(r.Near, r.Latitude, r.Longitude, r.RadiusMiles, r.Page, r.PageSize, search.CurrentValue))
            yield return e;
        if (r.Years is { } y && (y < 1 || y > metrics.CurrentValue.HistoryYears))
            yield return new("years", $"Years must be between 1 and {metrics.CurrentValue.HistoryYears}.");
        if (r.Search is { Length: > 100 })
            yield return new("search", "Search text is too long.");
    }
}

public sealed class GetExecutivesNearHandler(
    ICompanyRepository repository,
    INearbySearchService nearby,
    ICurrencyConverter fx,
    IOptionsMonitor<SearchOptions> searchOptions,
    IOptionsMonitor<MetricsOptions> metricsOptions) : IRequestHandler<GetExecutivesNearQuery, ExecutivesNearResponse>
{
    public async Task<ExecutivesNearResponse> HandleAsync(GetExecutivesNearQuery query, CancellationToken ct)
    {
        var r = query.Request;
        var o = searchOptions.CurrentValue;
        var (origin, originLabel) = await nearby.ResolveOriginAsync(r.Near, r.Latitude, r.Longitude, ct);
        var radius = r.RadiusMiles ?? o.DefaultRadiusMiles;
        var sort = r.Sort ?? ExecutiveSort.Pay;
        var page = r.Page ?? 1;
        var pageSize = r.PageSize ?? o.DefaultPageSize;
        var windowYears = r.Years ?? metricsOptions.CurrentValue.HistoryYears;

        // 1. Nearby companies (optionally one sector).
        var inRange = await nearby.FindCompaniesAsync(origin, radius, ct);
        var nearbyCompanies = (await repository.GetCompaniesAsync(inRange.Keys, ct))
            .Where(c => string.IsNullOrWhiteSpace(r.Sector) || string.Equals(c.Sector, r.Sector.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);

        // 2. Everyone who has been a named executive at one of them, then their full history anywhere.
        var localRows = await repository.GetExecutiveCompensationAsync(nearbyCompanies.Keys, ct);
        var people = localRows.Select(x => x.PersonId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var allRows = await repository.GetCompensationForPeopleAsync(people, ct);

        // Capped at the current year so one bad row (a misread "2042") can't slide everyone else out of the window.
        var latestYearInData = allRows.Count == 0 ? DateTime.UtcNow.Year : Math.Min(allRows.Max(x => x.Year), DateTime.UtcNow.Year);
        var fromYear = latestYearInData - windowYears + 1;
        var otherCompanies = (await repository.GetCompaniesAsync(
                allRows.Select(x => x.CompanyId).Where(id => !nearbyCompanies.ContainsKey(id)).Distinct(StringComparer.OrdinalIgnoreCase), ct))
            .ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        Company? CompanyOf(string id) => nearbyCompanies.GetValueOrDefault(id) ?? otherCompanies.GetValueOrDefault(id);

        var rows = new List<ExecutiveSummaryDto>();
        foreach (var personRows in allRows.GroupBy(x => x.PersonId, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = personRows.OrderBy(x => x.Year).ThenByDescending(x => x.Total).ToList();
            var latest = ordered[^1];
            var isCurrent = nearbyCompanies.ContainsKey(latest.CompanyId);
            if (!isCurrent && !r.IncludeFormer) continue;

            // The nearby role we show: the current one, or their most recent nearby one.
            var shown = isCurrent ? latest : ordered.Last(x => nearbyCompanies.ContainsKey(x.CompanyId));
            var company = nearbyCompanies[shown.CompanyId];
            var hit = inRange[shown.CompanyId];

            if (!string.IsNullOrWhiteSpace(r.Search) &&
                !latest.ExecutiveName.Contains(r.Search.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !shown.Title.Contains(r.Search.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.Role is { } role && !ExecutiveRoles.Holds(shown.Title, role)) continue;

            // Pay per year (a mid-year move can mean two rows in one year).
            var byYear = ordered.Where(x => x.Year >= fromYear)
                .GroupBy(x => x.Year)
                .Select(g => new PayPointDto(g.Key, g.Sum(x => x.Total), CompanyOf(g.MaxBy(x => x.Total)!.CompanyId)?.Ticker ?? g.First().CompanyId))
                .OrderBy(p => p.Year)
                .ToList();
            if (byYear.Count == 0) continue;

            var last = byYear[^1];
            var prev = byYear.Count > 1 && byYear[^2].Year == last.Year - 1 ? byYear[^2] : null;
            var growth = prev is { Total: > 0 } ? Math.Round(last.Total / prev.Total - 1, 4) : (decimal?)null;

            rows.Add(new ExecutiveSummaryDto(
                latest.PersonId, latest.ExecutiveName, shown.Title,
                new CompanyRefDto(company.Ticker, company.Name, company.Sector, company.PayCurrency ?? company.Currency),
                hit.NearestLocation.ToDto(), hit.DistanceMiles, isCurrent,
                last.Year, last.Total, growth,
                byYear.Sum(p => p.Total), byYear.Count,
                ordered.Where(x => x.Year >= fromYear).Select(x => x.CompanyId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                byYear));
        }

        // Pay can be in different currencies (UK groups pay in pounds, some in dollars) — total it in the main one.
        var currency = fx.Dominant(rows.Select(x => x.Company.Currency));
        var latestPays = rows.Where(x => x.IsCurrent).Select(x => fx.Convert(x.LatestTotalPay, x.Company.Currency, currency)).Order().ToList();
        var summary = new ExecutivesNearSummaryDto(
            rows.Count,
            rows.Select(x => x.Company.Ticker).Distinct().Count(),
            latestPays.Sum(),
            Median(latestPays),
            rows.Count == 0 ? null : rows.Max(x => x.LatestYear),
            currency,
            rows.Any(x => !string.Equals(x.Company.Currency, currency, StringComparison.OrdinalIgnoreCase)));

        var items = Sort(rows, sort).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new ExecutivesNearResponse(origin.ToDto(), originLabel, radius, sort, page, pageSize, rows.Count, summary, items);
    }

    private static decimal? Median(IReadOnlyList<decimal> sorted) => sorted.Count switch
    {
        0 => null,
        var n when n % 2 == 1 => sorted[n / 2],
        var n => (sorted[n / 2 - 1] + sorted[n / 2]) / 2
    };

    private IEnumerable<ExecutiveSummaryDto> Sort(IEnumerable<ExecutiveSummaryDto> rows, ExecutiveSort sort) => sort switch
    {
        ExecutiveSort.TotalPay => rows.OrderByDescending(x => fx.ToUsd(x.WindowTotalPay, x.Company.Currency)),
        ExecutiveSort.PayGrowth => rows.OrderByDescending(x => x.PayGrowthYoY ?? decimal.MinValue),
        ExecutiveSort.Distance => rows.OrderBy(x => x.DistanceMiles).ThenByDescending(x => fx.ToUsd(x.LatestTotalPay, x.Company.Currency)),
        ExecutiveSort.Name => rows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
        _ => rows.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => fx.ToUsd(x.LatestTotalPay, x.Company.Currency))
    };
}

// ---------- One executive's career and pay ----------

public sealed record GetExecutiveQuery(string PersonId) : IRequest<ExecutiveDetailDto>, ICacheableRequest, ITrackedRequest<ExecutiveDetailDto>
{
    public string CacheKey => $"person|{PersonId.ToUpperInvariant()}";
    public string CacheProfile => "Company";
    public AnalyticsAction Describe(ExecutiveDetailDto response) =>
        new("executive_view", response.PersonId, $"{response.Name} ({response.CurrentCompany.Name})");
}

public sealed class GetExecutiveHandler(ICompanyRepository repository) : IRequestHandler<GetExecutiveQuery, ExecutiveDetailDto>
{
    public async Task<ExecutiveDetailDto> HandleAsync(GetExecutiveQuery q, CancellationToken ct)
    {
        var rows = (await repository.GetCompensationForPeopleAsync([q.PersonId], ct)).OrderBy(x => x.Year).ThenByDescending(x => x.Total).ToList();
        if (rows.Count == 0) throw new NotFoundException($"No executive with id '{q.PersonId}'.");

        var person = await repository.GetPersonAsync(q.PersonId, ct);
        var companies = (await repository.GetCompaniesAsync(rows.Select(x => x.CompanyId).Distinct(StringComparer.OrdinalIgnoreCase), ct))
            .ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        CompanyRefDto Ref(string id) => companies.TryGetValue(id, out var c) ? new CompanyRefDto(c.Ticker, c.Name, c.Sector, c.PayCurrency ?? c.Currency) : new CompanyRefDto(id, id, "");

        // Roles: consecutive years at the same company (a return later becomes a new role).
        var roles = new List<ExecutiveRoleDto>();
        foreach (var row in rows)
        {
            var current = roles.Count > 0 ? roles[^1] : null;
            if (current is not null && current.Company.Ticker == Ref(row.CompanyId).Ticker && row.Year <= current.ToYear + 1)
                roles[^1] = current with { ToYear = row.Year, Title = row.Title, TotalPay = current.TotalPay + row.Total };
            else
                roles.Add(new ExecutiveRoleDto(Ref(row.CompanyId), row.Title, row.Year, row.Year, row.Total));
        }

        var latest = rows[^1];
        return new ExecutiveDetailDto(
            q.PersonId,
            person?.Name ?? latest.ExecutiveName,
            person?.SecCik,
            latest.Title,
            Ref(latest.CompanyId),
            rows[0].Year,
            latest.Year,
            rows.Sum(x => x.Total),
            rows.Select(x => x.Year).Distinct().Count(),
            roles.OrderByDescending(r => r.ToYear).ToList(),
            rows.OrderByDescending(x => x.Year)
                .Select(x => new ExecutivePayYearDto(x.Year, Ref(x.CompanyId), x.Title, x.Salary, x.Bonus, x.StockAwards, x.Other, x.Total, x.SourceFiling))
                .ToList());
    }
}
