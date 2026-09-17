using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Core.Services;

/// <summary>
/// "The top-paid CEO here earns a typical worker's yearly pay every N hours": the best-paid chief executive (UK: executive director) of the companies
/// headquartered in a search, from each company's latest reported year, compared with their home country's median pay.
/// </summary>
public interface ITopPaidCeoService
{
    Task<TopPaidCeoDto?> FindAsync(IReadOnlyList<Company> headquarteredHere, CancellationToken ct = default);
}

public sealed partial class TopPaidCeoService(ICompanyRepository repository, ICurrencyConverter fx, IOptionsMonitor<BenchmarkOptions> benchmarks)
    : ITopPaidCeoService
{
    /// <summary>Hours in an average calendar year (365.25 × 24) — "every N hours" counts nights and weekends too.</summary>
    public const double HoursPerYear = 8766;

    public async Task<TopPaidCeoDto?> FindAsync(IReadOnlyList<Company> headquarteredHere, CancellationToken ct = default)
    {
        if (headquarteredHere.Count == 0) return null;
        var companies = headquarteredHere.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var rows = await repository.GetExecutiveCompensationAsync(companies.Keys, ct);
        if (rows.Count == 0) return null;

        // Only recent pay counts: a company that stopped filing years ago shouldn't hold the title. Capped at this year so one
        // misread year ("2042") can't push everyone else out.
        var newestYear = Math.Min(rows.Max(r => r.Year), DateTime.UtcNow.Year);
        var chiefs = rows
            .GroupBy(r => r.CompanyId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Company: companies[g.Key], Latest: g.Where(r => r.Year <= newestYear).Select(r => r.Year).DefaultIfEmpty().Max(), Rows: g))
            .Where(x => x.Latest >= newestYear - 1)
            .SelectMany(x => x.Rows.Where(r => r.Year == x.Latest && r.Total > 0 && RoleOf(r.Title) is not null).Select(r => (x.Company, Row: r)))
            .ToList();
        if (chiefs.Count == 0) return null;
        var best = chiefs.MaxBy(x => fx.ToUsd(x.Row.Total, PayCurrency(x.Company)));

        var currency = PayCurrency(best.Company);
        var locations = await repository.GetLocationsAsync(best.Company.CompanyId, ct);
        var home = locations.FirstOrDefault(l => l.IsHeadquarters) ?? locations.FirstOrDefault();
        var country = home is null ? null : CountryOf(home, best.Company);

        return new TopPaidCeoDto(best.Row.PersonId, best.Row.ExecutiveName, best.Row.Title, best.Company.Ticker, best.Company.Name,
            best.Row.Year, best.Row.Total, currency, RoleOf(best.Row.Title)!, Compare(best.Row.Total, currency, country));
    }

    private MedianPayComparisonDto? Compare(decimal pay, string payCurrency, string? country)
    {
        if (country is null || !benchmarks.CurrentValue.MedianPay.TryGetValue(country, out var median) || median.AnnualPay <= 0) return null;
        var converted = fx.Convert(pay, payCurrency, median.Currency);
        // Someone paid less than the median makes no "every N hours" story.
        if (converted <= median.AnnualPay) return null;
        var hours = HoursPerYear * (double)(median.AnnualPay / converted);
        return new MedianPayComparisonDto(country.ToUpperInvariant(), median.Description, median.AnnualPay, median.Currency, median.Period,
            median.Source, median.SourceUrl, Math.Round(hours, 3),
            !string.Equals(payCurrency, median.Currency, StringComparison.OrdinalIgnoreCase));
    }

    private static string PayCurrency(Company c) => c.PayCurrency ?? c.Currency;

    /// <summary>
    /// "CEO"; or "executive director" for UK annual reports, which list each executive director without saying which one is the chief
    /// executive; null for anyone else.
    /// </summary>
    public static string? RoleOf(string title) =>
        IsChiefExecutive(title) ? "CEO" : string.Equals(title.Trim(), "Executive Director", StringComparison.OrdinalIgnoreCase) ? "executive director" : null;

    /// <summary>"Chief Executive Officer", "President and CEO", "Group Chief Executive" — not former, deputy or vice chiefs.</summary>
    public static bool IsChiefExecutive(string title) =>
        title.Length <= 120 && ChiefExecutive().IsMatch(title) && !NotTheChief().IsMatch(title);

    [GeneratedRegex(@"chief\s+executive|\bCEO\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChiefExecutive();

    [GeneratedRegex(@"\b(former|deputy|vice|assistant|retired|outgoing|incoming)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheChief();

    private static readonly HashSet<string> CanadianProvinces = new(StringComparer.OrdinalIgnoreCase)
        { "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT" };

    /// <summary>
    /// The country code of a location, as the coverage areas spell it. US and Canadian locations store a state or province;
    /// the others store the country ("UK", "FR", "AU"…). "NL" is Newfoundland for a dollar company, the Netherlands for a euro one.
    /// </summary>
    public static string CountryOf(CompanyLocation location, Company company) => location.State.ToUpperInvariant() switch
    {
        "NL" when string.Equals(company.Currency, "EUR", StringComparison.OrdinalIgnoreCase) => "NL",
        var s when CanadianProvinces.Contains(s) => "CA",
        "UK" or "GB" => "UK",
        "FR" or "IT" or "ES" or "AU" or "NZ" => location.State.ToUpperInvariant(),
        _ => "US"
    };
}
