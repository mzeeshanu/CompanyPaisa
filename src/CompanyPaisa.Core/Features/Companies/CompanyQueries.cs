using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Mapping;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Features.Companies;

// ---------- Company profile ----------

public sealed record GetCompanyQuery(string Ticker) : IRequest<CompanyDetailDto>, ICacheableRequest, ITrackedRequest<CompanyDetailDto>
{
    public string CacheKey => $"company|{Ticker.ToUpperInvariant()}";
    public string CacheProfile => "Company";
    public AnalyticsAction Describe(CompanyDetailDto response) => new("company_view", response.Ticker, response.Name);
}

public sealed class GetCompanyHandler(ICompanyRepository repository, IFinancialMetricsService metrics)
    : IRequestHandler<GetCompanyQuery, CompanyDetailDto>
{
    public async Task<CompanyDetailDto> HandleAsync(GetCompanyQuery query, CancellationToken ct)
    {
        var c = await repository.GetCompanyAsync(query.Ticker, ct) ?? throw CompanyNotFound(query.Ticker);
        var locations = await repository.GetLocationsAsync(c.CompanyId, ct);
        var indicators = metrics.Compute(await repository.GetFinancialsAsync(c.CompanyId, ct));
        // Pay ratios come from US proxy statements, which state pay in US dollars.
        var workerPay = (await repository.GetWorkerPayAsync(c.CompanyId, ct)).OrderBy(w => w.Year)
            .Select(w => new WorkerPayDto(w.Year, w.MedianEmployeePay, w.CeoPay, w.Ratio, "USD", w.SourceFiling)).ToList();
        var salaryTitles = (await repository.GetJobSalariesAsync(c.CompanyId, ct)).Count(j => j.City is null);

        return new CompanyDetailDto(c.Ticker, c.Name, c.Exchange, c.Sector, c.Industry, c.Website, c.Employees, c.MarketCap,
            c.Description, c.Currency, c.FiscalYearEnd, c.AsOfDate,
            locations.OrderBy(l => l.IsHeadquarters ? 0 : 1).ThenBy(l => l.City).Select(l => l.ToDto()).ToList(),
            indicators.ToDto(), c.PayCurrency ?? c.Currency, c.CareersUrl, workerPay, salaryTitles);
    }

    internal static NotFoundException CompanyNotFound(string ticker) => new($"No company with ticker '{ticker}'.");
}

// ---------- Job salaries ----------

public sealed record GetJobSalariesQuery(string Ticker) : IRequest<JobSalariesResponse>, ICacheableRequest
{
    public string CacheKey => $"salaries|{Ticker.ToUpperInvariant()}";
    public string CacheProfile => "Company";
}

public sealed class GetJobSalariesHandler(ICompanyRepository repository) : IRequestHandler<GetJobSalariesQuery, JobSalariesResponse>
{
    public async Task<JobSalariesResponse> HandleAsync(GetJobSalariesQuery q, CancellationToken ct)
    {
        var c = await repository.GetCompanyAsync(q.Ticker, ct) ?? throw GetCompanyHandler.CompanyNotFound(q.Ticker);
        var rows = await repository.GetJobSalariesAsync(c.CompanyId, ct);
        var source = rows.Count > 0 ? await repository.GetJobSalarySourceAsync(ct) : null;
        var places = rows.Where(r => r.City is not null).ToLookup(r => r.Title, StringComparer.Ordinal);
        var titles = rows.Where(r => r.City is null).OrderByDescending(r => r.Filings).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new JobSalaryDto(t.Title, t.Occupation, t.Filings, t.Low, t.Median, t.High, t.Min, t.Max,
                places[t.Title].OrderByDescending(p => p.Filings)
                    .Select(p => new JobSalaryPlaceDto(p.City!, p.State ?? "", p.Filings, p.Low, p.Median, p.High, p.Point?.Latitude, p.Point?.Longitude))
                    .ToList()))
            .ToList();
        // H-1B filings are US jobs, paid in US dollars.
        return new JobSalariesResponse(c.Ticker, "USD", source?.From, source?.To, source?.Source, titles);
    }
}

// ---------- Financial history ----------

public sealed record GetFinancialsQuery(string Ticker, PeriodType PeriodType, int? Years) : IRequest<FinancialsResponse>, ICacheableRequest
{
    public string CacheKey => $"fin|{Ticker.ToUpperInvariant()}|{PeriodType}|{Years}";
    public string CacheProfile => "Company";
}

public sealed class GetFinancialsValidator(IOptionsMonitor<MetricsOptions> options) : IRequestValidator<GetFinancialsQuery>
{
    public IEnumerable<ValidationError> Validate(GetFinancialsQuery q)
    {
        var max = options.CurrentValue.HistoryYears;
        if (q.Years is { } y && (y < 1 || y > max))
            yield return new("years", $"Years must be between 1 and {max}.");
    }
}

public sealed class GetFinancialsHandler(ICompanyRepository repository, IFinancialMetricsService metrics, IOptionsMonitor<MetricsOptions> options)
    : IRequestHandler<GetFinancialsQuery, FinancialsResponse>
{
    public async Task<FinancialsResponse> HandleAsync(GetFinancialsQuery q, CancellationToken ct)
    {
        var c = await repository.GetCompanyAsync(q.Ticker, ct) ?? throw GetCompanyHandler.CompanyNotFound(q.Ticker);
        var periods = metrics.WithGrowth(await repository.GetFinancialsAsync(c.CompanyId, ct), q.PeriodType);

        var years = q.Years ?? options.CurrentValue.HistoryYears;
        var fromYear = periods.Count == 0 ? 0 : periods[^1].Period.FiscalYear - years + 1;

        return new FinancialsResponse(c.Ticker, q.PeriodType,
            periods.Where(p => p.Period.FiscalYear >= fromYear).Select(p => p.Period.ToDto(p.RevenueGrowthYoY)).ToList());
    }
}

// ---------- Executive compensation ----------

public sealed record GetExecutivesQuery(string Ticker, int? Years) : IRequest<ExecutivesResponse>, ICacheableRequest
{
    public string CacheKey => $"exec|{Ticker.ToUpperInvariant()}|{Years}";
    public string CacheProfile => "Company";
}

public sealed class GetExecutivesValidator : IRequestValidator<GetExecutivesQuery>
{
    public IEnumerable<ValidationError> Validate(GetExecutivesQuery q)
    {
        if (q.Years is { } y && (y < 1 || y > 20))
            yield return new("years", "Years must be between 1 and 20.");
    }
}

public sealed class GetExecutivesHandler(ICompanyRepository repository) : IRequestHandler<GetExecutivesQuery, ExecutivesResponse>
{
    public async Task<ExecutivesResponse> HandleAsync(GetExecutivesQuery q, CancellationToken ct)
    {
        var c = await repository.GetCompanyAsync(q.Ticker, ct) ?? throw GetCompanyHandler.CompanyNotFound(q.Ticker);
        var rows = await repository.GetExecutiveCompensationAsync(c.CompanyId, ct);
        if (rows.Count == 0) return new ExecutivesResponse(c.Ticker, []);

        var fromYear = Math.Min(rows.Max(x => x.Year), DateTime.UtcNow.Year) - (q.Years ?? 5) + 1;
        var execs = rows
            .Where(x => x.Year >= fromYear)
            .GroupBy(x => x.PersonId)
            .Select(g =>
            {
                var latest = g.MaxBy(x => x.Year)!;
                return new ExecutiveDto(g.Key, latest.ExecutiveName, latest.Title,
                    g.OrderBy(x => x.Year)
                     .Select(x => new ExecutiveCompYearDto(x.Year, x.Salary, x.Bonus, x.StockAwards, x.Other, x.Total, x.SourceFiling))
                     .ToList());
            })
            .OrderByDescending(e => e.History[^1].Total)
            .ToList();

        return new ExecutivesResponse(c.Ticker, execs);
    }
}
