using System.Globalization;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Data.Excel;

namespace CompanyPaisa.Importer.Validation;

/// <summary>How bad a finding is. Errors are values that can't be right; warnings are worth a look.</summary>
public enum Severity { Error, Warning }

/// <summary>One data-quality check: how many rows failed it, and a few of them to look at.</summary>
public sealed record CheckResult(string Area, string Name, Severity Severity, string Explanation, int Count, IReadOnlyList<string> Examples);

/// <summary>Counts for the report's overview tables.</summary>
public sealed record Tally(string Title, IReadOnlyList<(string Key, int Count)> Rows);

public sealed record ValidationResult(IReadOnlyList<CheckResult> Checks, IReadOnlyList<Tally> Tallies)
{
    public int Errors => Checks.Where(c => c.Severity == Severity.Error).Sum(c => c.Count);
    public int Warnings => Checks.Where(c => c.Severity == Severity.Warning).Sum(c => c.Count);
}

/// <summary>
/// Sanity checks over the published data (every workbook merged, as the website sees it): impossible or implausible
/// numbers, locations outside their country, stale or missing history, pay rows that can't be right.
/// It never changes data — it reports, so a person can decide whether the importer or the source is at fault.
/// </summary>
public static class DataValidator
{
    private const int MaxExamples = 12;

    /// <param name="usdPer">Approximate US dollars per unit of each currency (the API's Currency:UsdPer).</param>
    /// <param name="today">Injected so tests are stable.</param>
    public static ValidationResult Validate(WorkbookContents data, IReadOnlyDictionary<string, decimal> usdPer, DateOnly today)
    {
        var checks = new List<CheckResult>();
        var companies = data.Companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var hqs = data.Locations.Where(l => l.IsHeadquarters).GroupBy(l => l.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var finByCompany = data.Financials.GroupBy(f => f.CompanyId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        decimal Usd(string companyId, decimal amount) =>
            amount * usdPer.GetValueOrDefault(companies.TryGetValue(companyId, out var c) ? c.Currency : "USD", 1m);
        string Name(string id) => companies.TryGetValue(id, out var c) ? $"{id} ({c.Name})" : id;

        void Check(string area, string name, Severity severity, string why, IEnumerable<string> failures)
        {
            var list = failures.ToList();
            checks.Add(new CheckResult(area, name, severity, why, list.Count, list.Take(MaxExamples).ToList()));
        }

        // ---------------- Companies & locations ----------------
        Check("Companies", "Currency without an exchange rate", Severity.Error,
            "The site can't add up or rank amounts in a currency it has no rate for.",
            data.Companies.Where(c => !usdPer.ContainsKey(c.Currency) || (c.PayCurrency is { } p && !usdPer.ContainsKey(p)))
                .Select(c => $"{Name(c.CompanyId)}: {c.Currency}{(c.PayCurrency is { } pc ? $" / pay {pc}" : "")}"));

        var located = data.Locations.Select(l => l.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Check("Companies", "No location at all", Severity.Error,
            "A company without any location can never appear in a search.",
            data.Companies.Where(c => !located.Contains(c.CompanyId)).Select(c => Name(c.CompanyId)));

        Check("Companies", "Only a local office, no headquarters", Severity.Warning,
            "Expected for hand-added offices of companies based abroad (e.g. NICE in Utah); anything else is a missing headquarters.",
            data.Companies.Where(c => located.Contains(c.CompanyId) && !hqs.ContainsKey(c.CompanyId)).Select(c => Name(c.CompanyId)));

        Check("Companies", "Same name under two ids", Severity.Warning,
            "Usually two share classes or a parent and its subsidiary listed separately — one company shown twice.",
            data.Companies.GroupBy(c => NormaliseName(c.Name)).Where(g => g.Count() > 1 && g.Key.Length > 0)
                .Select(g => string.Join(", ", g.Select(c => c.CompanyId)) + $" — {g.First().Name}"));

        Check("Locations", "Outside its country", Severity.Error,
            "The map point is outside the company's country — a wrong postcode or a geocoding mix-up.",
            data.Locations.Where(l => CountryOf(l, companies.GetValueOrDefault(l.CompanyId)) is { } cc && Bounds.TryGetValue(cc, out var b) && !b.Contains(l.Point))
                .Select(l => $"{Name(l.CompanyId)}: {l.City}, {l.State} ({l.Point.Latitude:0.00}, {l.Point.Longitude:0.00})"));

        Check("Locations", "Missing city", Severity.Warning, "Shown as a blank place name on the site.",
            data.Locations.Where(l => string.IsNullOrWhiteSpace(l.City)).Select(l => $"{Name(l.CompanyId)} {l.LocationId}"));

        // ---------------- Financials ----------------
        Check("Financials", "No figures at all", Severity.Error,
            "A company with no revenue history shows empty charts; the importer should have left it out.",
            data.Companies.Where(c => !finByCompany.ContainsKey(c.CompanyId)).Select(c => Name(c.CompanyId)));

        Check("Financials", "Negative revenue", Severity.Warning,
            "Reported revenue below zero. Real for mortgage REITs and energy producers, whose revenue includes investment or hedging losses; otherwise a sign error.",
            data.Financials.Where(f => f.Revenue < 0).Select(f => $"{Name(f.CompanyId)} {f.Label}: {f.Revenue:N0}"));

        Check("Financials", "Period in the future", Severity.Error, "A fiscal year after this one can't have been reported yet.",
            data.Financials.Where(f => f.FiscalYear > today.Year).Select(f => $"{Name(f.CompanyId)} {f.Label}"));

        Check("Financials", "Duplicate period", Severity.Error, "Two rows for the same company and period; one of them is wrong.",
            data.Financials.GroupBy(f => (Id: f.CompanyId.ToUpperInvariant(), f.PeriodType, f.FiscalYear, f.FiscalQuarter)).Where(g => g.Count() > 1)
                .Select(g => $"{Name(g.Key.Id)} {g.First().Label} ×{g.Count()}"));

        Check("Financials", "Implausibly large revenue", Severity.Warning,
            "Annual revenue above $800bn (US-dollar equivalent) — only a handful of companies on earth are near that; often a unit error.",
            data.Financials.Where(f => f.PeriodType == PeriodType.Annual && Usd(f.CompanyId, f.Revenue) > 800_000_000_000m)
                .Select(f => $"{Name(f.CompanyId)} {f.Label}: {Money(f.Revenue)} {companies.GetValueOrDefault(f.CompanyId)?.Currency}"));

        // Losses far bigger than revenue are normal for drug developers; profits far bigger than revenue are not.
        Check("Financials", "Profit far bigger than revenue", Severity.Warning,
            "Net profit more than 3× revenue for a company with ≥ $10M revenue — possible for holding companies and one-off gains, often a revenue sub-line.",
            data.Financials.Where(f => f.PeriodType == PeriodType.Annual && Usd(f.CompanyId, f.Revenue) >= 10_000_000m && f.NetIncome > 3 * f.Revenue)
                .Select(f => $"{Name(f.CompanyId)} {f.Label}: revenue {Money(f.Revenue)}, net income {Money(f.NetIncome)}"));

        Check("Financials", "Revenue jumps 10× in a year", Severity.Warning,
            "Year-on-year revenue up or down more than tenfold (both years ≥ $10M) — a real acquisition, or a switch of revenue definition or units.",
            finByCompany.SelectMany(kv => kv.Value.Where(f => f.PeriodType == PeriodType.Annual).OrderBy(f => f.FiscalYear)
                    .Zip(kv.Value.Where(f => f.PeriodType == PeriodType.Annual).OrderBy(f => f.FiscalYear).Skip(1))
                    .Where(p => p.Second.FiscalYear == p.First.FiscalYear + 1 && Usd(kv.Key, Math.Min(p.First.Revenue, p.Second.Revenue)) >= 10_000_000m
                                && (p.Second.Revenue > 10 * p.First.Revenue || p.First.Revenue > 10 * p.Second.Revenue))
                    .Select(p => $"{Name(kv.Key)}: FY {p.First.FiscalYear} {Money(p.First.Revenue)} → FY {p.Second.FiscalYear} {Money(p.Second.Revenue)}")));

        Check("Financials", "Quarters don't add up to the year", Severity.Warning,
            "For December year-ends, the four quarters differ from the annual figure by more than 2% (revenue ≥ $10M). Usually a restatement: the year was restated later (a business sold, an accounting change) while the quarters are as first reported.",
            data.Companies.Where(c => c.FiscalYearEnd == "12-31" && finByCompany.ContainsKey(c.CompanyId)).SelectMany(c =>
                finByCompany[c.CompanyId].Where(f => f.PeriodType == PeriodType.Annual && Usd(c.CompanyId, f.Revenue) >= 10_000_000m).Select(a =>
                {
                    var q = finByCompany[c.CompanyId].Where(f => f.PeriodType == PeriodType.Quarterly && f.FiscalYear == a.FiscalYear).ToList();
                    if (q.Count != 4) return null;
                    var sum = q.Sum(f => f.Revenue);
                    return Math.Abs(sum - a.Revenue) > 0.02m * Math.Abs(a.Revenue) ? $"{Name(c.CompanyId)} FY {a.FiscalYear}: quarters {Money(sum)} vs year {Money(a.Revenue)}" : null;
                }).OfType<string>()));

        Check("Financials", "Out of date", Severity.Warning,
            "Newest annual figures are more than two years old — the company may have stopped reporting, been taken over, or changed filer.",
            finByCompany.Where(kv => kv.Value.Where(f => f.PeriodType == PeriodType.Annual).Select(f => f.FiscalYear).DefaultIfEmpty(0).Max() is var y && y > 0 && y < today.Year - 2)
                .Select(kv => $"{Name(kv.Key)}: latest FY {kv.Value.Where(f => f.PeriodType == PeriodType.Annual).Max(f => f.FiscalYear)}"));

        Check("Financials", "Annual figures missing, quarters only", Severity.Warning,
            "Shown with quarterly figures only; the headline 'annual revenue' comes from the last four quarters.",
            finByCompany.Where(kv => kv.Value.All(f => f.PeriodType == PeriodType.Quarterly)).Select(kv => Name(kv.Key)));

        // ---------------- Executive pay ----------------
        var people = data.People.Select(p => p.PersonId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Check("Pay", "Negative amount", Severity.Error, "Pay components and totals can't be negative in a summary compensation table.",
            data.Pay.Where(p => p.Total < 0 || p.Salary < 0 || p.Bonus < 0 || p.StockAwards < 0)
                .Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}: salary {p.Salary:N0}, bonus {p.Bonus:N0}, stock {p.StockAwards:N0}, total {p.Total:N0}"));

        Check("Pay", "Year in the future", Severity.Error, "Pay can't be reported for a year that hasn't ended.",
            data.Pay.Where(p => p.Year > today.Year).Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}"));

        Check("Pay", "Person record missing", Severity.Error, "A pay row whose person isn't in the People sheet can't be opened on the site.",
            data.Pay.Where(p => !people.Contains(p.PersonId)).Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} ({p.PersonId})"));

        Check("Pay", "Same person, company and year twice", Severity.Error, "Would double-count that year's pay.",
            data.Pay.GroupBy(p => (Company: p.CompanyId.ToUpperInvariant(), Person: p.PersonId.ToUpperInvariant(), p.Year)).Where(g => g.Count() > 1)
                .Select(g => $"{Name(g.Key.Company)} {g.First().ExecutiveName} {g.Key.Year} ×{g.Count()}"));

        Check("Pay", "Salary bigger than total", Severity.Warning,
            "The total should include salary (2% allowed for rounding); usually a misread column.",
            data.Pay.Where(p => p.Total > 0 && p.Salary > p.Total * 1.02m).Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}: salary {p.Salary:N0}, total {p.Total:N0}"));

        Check("Pay", "Very large total", Severity.Warning,
            "Total above $150M (US-dollar equivalent) in one year — happens (mega stock grants), but check it's not a unit error.",
            data.Pay.Where(p => p.Total * usdPer.GetValueOrDefault(PayCurrency(companies, p.CompanyId), 1m) > 150_000_000m)
                .OrderByDescending(p => p.Total).Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}: {Money(p.Total)}"));

        Check("Pay", "Tiny total", Severity.Warning,
            "Total between 1 and 10,000 — usually a table reported in thousands, read as units.",
            data.Pay.Where(p => p.Total is > 0 and < 10_000).Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}: {p.Total:N0}"));

        Check("Pay", "Name doesn't look like a person", Severity.Warning,
            "Digits, company words or table labels where a person's name should be.",
            data.Pay.Select(p => (p.CompanyId, p.ExecutiveName)).Distinct().Where(x => !LooksLikePerson(x.ExecutiveName))
                .Select(x => $"{Name(x.CompanyId)}: \"{x.ExecutiveName}\""));

        Check("Pay", "Pieces don't add up to the total", Severity.Warning,
            "Salary + bonus + stock + other differs from the filed total by more than 5%. The site shows the filed total; the difference is usually a column the parser folded into 'other'.",
            data.Pay.Where(p => p.Total > 0 && Math.Abs(p.Salary + p.Bonus + p.StockAwards + p.Other - p.Total) > 0.05m * p.Total)
                .Select(p => $"{Name(p.CompanyId)} {p.ExecutiveName} {p.Year}: pieces {p.Salary + p.Bonus + p.StockAwards + p.Other:N0} vs total {p.Total:N0}"));

        // ---------------- Tallies ----------------
        var withPay = data.Pay.Select(p => p.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tallies = new List<Tally>
        {
            Count("Companies by country", data.Companies.Select(c => CountryOf(hqs.GetValueOrDefault(c.CompanyId), c) ?? "?")),
            Count("Companies by exchange", data.Companies.Select(c => c.Exchange)),
            Count("Companies by reporting currency", data.Companies.Select(c => c.Currency)),
            Count("Companies by sector", data.Companies.Select(c => c.Sector)),
            Count("Companies with executive pay, by country", data.Companies.Where(c => withPay.Contains(c.CompanyId))
                .Select(c => CountryOf(hqs.GetValueOrDefault(c.CompanyId), c) ?? "?")),
            Count("Years of annual history per company", finByCompany.Values.Select(v => v.Count(f => f.PeriodType == PeriodType.Annual)).Select(n => n switch
            {
                0 => "0 (quarters only)", <= 2 => "1–2", <= 5 => "3–5", <= 9 => "6–9", _ => "10+"
            })),
            Count("Newest annual figures", finByCompany.Values.Select(v => v.Where(f => f.PeriodType == PeriodType.Annual).Select(f => f.FiscalYear).DefaultIfEmpty(0).Max())
                .Select(y => y == 0 ? "none" : y < today.Year - 2 ? $"{today.Year - 3} or older" : $"FY {y}")),
            new("Rows", [("Companies", data.Companies.Count), ("Locations", data.Locations.Count),
                ("Annual periods", data.Financials.Count(f => f.PeriodType == PeriodType.Annual)),
                ("Quarterly periods", data.Financials.Count(f => f.PeriodType == PeriodType.Quarterly)),
                ("Pay rows", data.Pay.Count), ("People", data.People.Count)])
        };
        return new ValidationResult(checks, tallies);
    }

    private static Tally Count(string title, IEnumerable<string> keys) =>
        new(title, keys.GroupBy(k => k).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key).ToList());

    private static string PayCurrency(IReadOnlyDictionary<string, Company> companies, string id) =>
        companies.TryGetValue(id, out var c) ? c.PayCurrency ?? c.Currency : "USD";

    /// <summary>Country from the ticker suffix (UK/Europe) or the location's state/province code.</summary>
    internal static string? CountryOf(CompanyLocation? location, Company? company)
    {
        var id = company?.CompanyId ?? location?.CompanyId ?? "";
        foreach (var (suffix, country) in Suffixes)
            if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return country;
        var state = location?.State.ToUpperInvariant();
        return state switch
        {
            null => null,
            "UK" => "GB",
            "AU" or "NZ" => state,
            _ when CanadianProvinces.Contains(state) => "CA",
            { Length: 2 } => "US",
            _ => null
        };
    }

    private static readonly (string Suffix, string Country)[] Suffixes = [(".L", "GB"), (".PA", "FR"), (".AS", "NL"), (".MI", "IT"), (".MC", "ES")];
    private static readonly HashSet<string> CanadianProvinces = ["AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT"];

    /// <summary>Generous boxes around each country (including Alaska, Hawaii, Puerto Rico, Corsica, the Canaries).</summary>
    private static readonly Dictionary<string, GeoBoundingBox> Bounds = new()
    {
        ["US"] = new(17, 72, -180, -64), ["CA"] = new(41, 84, -142, -52), ["GB"] = new(49, 61.5, -9, 2.5),
        ["FR"] = new(41, 51.5, -5.5, 10), ["NL"] = new(50.6, 53.7, 3.2, 7.3), ["IT"] = new(35, 47.2, 6.5, 18.6),
        ["ES"] = new(27.5, 44, -18.5, 4.5), ["AU"] = new(-44, -9, 112, 154), ["NZ"] = new(-48, -34, 166, 179)
    };

    private static readonly string[] NotPersonWords = ["total", "inc", "llc", "ltd", "plc", "corp", "company", "officers", "directors", "named", "executive", "average", "compensation", "salary"];

    internal static bool LooksLikePerson(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsDigit)) return false;
        var words = name.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 6 && !words.Any(w => NotPersonWords.Contains(w.Trim('.').ToLowerInvariant()));
    }

    private static string NormaliseName(string name)
    {
        var n = new string(name.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray());
        foreach (var w in new[] { " inc", " corp", " corporation", " co", " ltd", " plc", " sa", " nv", " spa", " se" })
            if (n.EndsWith(w, StringComparison.Ordinal)) n = n[..^w.Length];
        return n.Trim();
    }

    private static string Money(decimal v) => Math.Abs(v) switch
    {
        >= 1_000_000_000 => (v / 1_000_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "bn",
        >= 1_000_000 => (v / 1_000_000).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        _ => v.ToString("N0", CultureInfo.InvariantCulture)
    };
}
