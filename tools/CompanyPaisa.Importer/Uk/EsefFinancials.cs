using System.Globalization;
using System.Text.Json;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Uk;

/// <summary>Annual figures from one ESEF report's xBRL-JSON: the year reported and the comparative year before it.</summary>
public sealed record EsefYear(int FiscalYear, DateOnly PeriodEnd, decimal Revenue, decimal NetIncome, decimal? OperatingIncome, decimal? Eps,
    string Currency, string RevenueConcept, IReadOnlyDictionary<string, decimal> RevenueCandidates)
{
    /// <summary>The same year measured with another revenue concept, when the report tagged it (keeps a company's years comparable).</summary>
    public EsefYear WithRevenueConcept(string concept) =>
        RevenueCandidates.TryGetValue(concept, out var v) ? this with { Revenue = v, RevenueConcept = concept } : this;
}

public static class EsefFinancials
{
    /// <summary>Revenue concepts in order of preference. Banks and insurers rarely tag plain "Revenue".</summary>
    internal static readonly string[] RevenueConcepts =
    [
        "ifrs-full:Revenue", "ifrs-full:RevenueFromContractsWithCustomers", "ifrs-full:InsuranceRevenue",
        "ifrs-full:RevenueFromSaleOfGoods", "ifrs-full:RevenueFromRenderingOfServices",
        "ifrs-full:InterestRevenueCalculatedUsingEffectiveInterestMethod", "ifrs-full:RevenueFromInterest"
    ];

    private static readonly string[] NetIncomeConcepts = ["ifrs-full:ProfitLossAttributableToOwnersOfParent", "ifrs-full:ProfitLoss"];
    private const string OperatingConcept = "ifrs-full:ProfitLossFromOperatingActivities";
    private const string EpsConcept = "ifrs-full:BasicEarningsLossPerShare";

    private sealed record Fact(string Concept, DateOnly Start, DateOnly End, string Unit, decimal Value);

    public static List<EsefYear> Extract(string json, DateOnly reportPeriodEnd)
    {
        using var doc = JsonDocument.Parse(json);
        var facts = new List<Fact>();
        foreach (var f in doc.RootElement.GetProperty("facts").EnumerateObject().Select(p => p.Value))
        {
            var dims = f.GetProperty("dimensions");
            // Company-level figures only: any extra axis (segment, restatement member…) means a breakdown.
            if (dims.EnumerateObject().Any(d => d.Name is not ("concept" or "entity" or "period" or "unit" or "language"))) continue;
            var concept = dims.GetProperty("concept").GetString() ?? "";
            if (!RevenueConcepts.Contains(concept) && !NetIncomeConcepts.Contains(concept) && concept != OperatingConcept && concept != EpsConcept) continue;
            if (!dims.TryGetProperty("period", out var p) || p.GetString() is not { } period || !period.Contains('/')) continue;
            var parts = period.Split('/');
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var endExclusive)) continue;
            if (!f.TryGetProperty("value", out var v) ||
                !decimal.TryParse(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            // xBRL-JSON periods end at midnight of the next day.
            facts.Add(new Fact(concept, DateOnly.FromDateTime(start), DateOnly.FromDateTime(endExclusive).AddDays(-1),
                dims.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "", value));
        }

        var years = new List<EsefYear>();
        // The reported year and its comparative: annual-length periods ending ~0 and ~1 year before the report date.
        foreach (var yearsBack in new[] { 0, 1 })
        {
            var target = reportPeriodEnd.AddYears(-yearsBack);
            var inYear = facts.Where(f => Math.Abs(f.End.DayNumber - target.DayNumber) <= 10 && (f.End.DayNumber - f.Start.DayNumber) is >= 340 and <= 380).ToList();
            // Plain "Revenue" when tagged; otherwise the largest candidate — banks tag a fee-income sub-line
            // (HSBC's $3bn) alongside interest income, and the sub-line badly understates the business.
            var money = inYear.Where(f => f.Unit.StartsWith("iso4217:", StringComparison.Ordinal) && f.Value > 0).ToList();
            var revenue = money.FirstOrDefault(f => f.Concept == RevenueConcepts[0])
                          ?? money.Where(f => RevenueConcepts.Contains(f.Concept)).MaxBy(f => f.Value);
            var net = NetIncomeConcepts.Select(c => inYear.FirstOrDefault(f => f.Concept == c && f.Unit == revenue?.Unit)).FirstOrDefault(f => f is not null);
            if (revenue is null || net is null) continue;
            var end = revenue.End;
            var candidates = money.Where(f => RevenueConcepts.Contains(f.Concept) && f.Unit == revenue.Unit)
                .GroupBy(f => f.Concept).ToDictionary(g => g.Key, g => g.First().Value);
            years.Add(new EsefYear(FiscalYearOf(end), end, revenue.Value, net.Value,
                inYear.FirstOrDefault(f => f.Concept == OperatingConcept && f.Unit == revenue.Unit)?.Value,
                inYear.FirstOrDefault(f => f.Concept == EpsConcept)?.Value,
                revenue.Unit["iso4217:".Length..], revenue.Concept, candidates));
        }
        return years;
    }

    /// <summary>A 52/53-week year ending in the first days of January belongs to the year before.</summary>
    internal static int FiscalYearOf(DateOnly end) => end is { Month: 1, Day: <= 7 } ? end.Year - 1 : end.Year;

    public static FinancialPeriod ToPeriod(string companyId, EsefYear y, string source) => new()
    {
        CompanyId = companyId, PeriodType = PeriodType.Annual, FiscalYear = y.FiscalYear, Revenue = y.Revenue, NetIncome = y.NetIncome,
        OperatingIncome = y.OperatingIncome, Eps = y.Eps, SourceFiling = source
    };
}
