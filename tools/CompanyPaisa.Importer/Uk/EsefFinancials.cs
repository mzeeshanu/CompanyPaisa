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

public static partial class EsefFinancials
{
    /// <summary>
    /// Revenue concepts: plain "Revenue" when tagged, else the largest of the rest. Banks and insurers rarely tag plain
    /// "Revenue"; nor do companies whose top line is "Revenue and operating income" (Aker BP, Solstad, Avanza) or rent
    /// (CA Immo, NEPI Rockcastle) — without those lines their interest income passed for revenue (Aker BP's $2.5 million).
    /// </summary>
    internal static readonly string[] RevenueConcepts =
    [
        "ifrs-full:Revenue", "ifrs-full:RevenueFromContractsWithCustomers", "ifrs-full:RevenueAndOperatingIncome",
        "ifrs-full:InsuranceRevenue", "ifrs-full:RevenueFromInsuranceContractsIssuedWithoutReductionForReinsuranceHeld",
        "ifrs-full:RevenueFromSaleOfGoods", "ifrs-full:RevenueFromRenderingOfServices", "ifrs-full:RentalIncomeFromInvestmentProperty",
        "ifrs-full:InterestRevenueCalculatedUsingEffectiveInterestMethod", "ifrs-full:RevenueFromInterest"
    ];

    private static readonly string[] NetIncomeConcepts = ["ifrs-full:ProfitLossAttributableToOwnersOfParent", "ifrs-full:ProfitLoss"];
    private const string OperatingConcept = "ifrs-full:ProfitLossFromOperatingActivities";
    private const string EpsConcept = "ifrs-full:BasicEarningsLossPerShare";
    private const string GrossProfitConcept = "ifrs-full:GrossProfit", CostOfSalesConcept = "ifrs-full:CostOfSales";
    private const string FeeIncomeConcept = "ifrs-full:FeeAndCommissionIncome", FeeExpenseConcept = "ifrs-full:FeeAndCommissionExpense";

    /// <summary>A financial company's side lines, which pass for revenue when its fees aren't tagged as revenue.</summary>
    private static readonly string[] SideLines =
        ["ifrs-full:InsuranceRevenue", "ifrs-full:RevenueFromInterest", "ifrs-full:InterestRevenueCalculatedUsingEffectiveInterestMethod"];

    /// <summary>Filers whose reports are in kroner and tagged as euros (by LEI): their euro figures are read as DKK.</summary>
    private static readonly HashSet<string> KronerTaggedAsEuros = ["213800V2R9WMMZASKK57", "213800GJODT6FV8QM841"];

    private sealed record Fact(string Concept, DateOnly Start, DateOnly End, string Unit, decimal Value, bool Statutory = false);

    public static List<EsefYear> Extract(string json, DateOnly reportPeriodEnd)
    {
        using var doc = JsonDocument.Parse(json);
        var facts = new List<Fact>();
        var grossLines = new List<Fact>();   // kept apart: they prove a revenue, they don't date the report
        // A report whose xBRL-JSON has no facts (a failed conversion) simply contributes nothing.
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("facts", out var allFacts) || allFacts.ValueKind != JsonValueKind.Object) return [];
        foreach (var f in allFacts.EnumerateObject().Select(p => p.Value))
        {
            if (!f.TryGetProperty("dimensions", out var dims) || !dims.TryGetProperty("concept", out _)) continue;
            // Company-level figures only: any extra axis (segment, restatement member…) means a breakdown — except a company's
            // own income-statement columns whose member is the statutory total (Centrica since 2024 tags revenue only as
            // "Business performance", "Exceptional items" and "ResultsForTheYear"); a plain figure still wins over it.
            var extra = dims.EnumerateObject().Where(d => d.Name is not ("concept" or "entity" or "period" or "unit" or "language")).ToList();
            var statutory = extra.Count == 1 && !extra[0].Name.StartsWith("ifrs-full:", StringComparison.Ordinal) &&
                            StatutoryMember().IsMatch(extra[0].Value.GetString() ?? "");
            if (extra.Count > 0 && !statutory) continue;
            var concept = dims.GetProperty("concept").GetString() ?? "";
            var grossLine = concept is GrossProfitConcept or CostOfSalesConcept or FeeIncomeConcept or FeeExpenseConcept;
            if (grossLine && statutory) continue;
            if (!grossLine && !RevenueConcepts.Contains(concept) && !NetIncomeConcepts.Contains(concept) && concept != OperatingConcept && concept != EpsConcept) continue;
            if (!dims.TryGetProperty("period", out var p) || p.GetString() is not { } period || !period.Contains('/')) continue;
            var parts = period.Split('/');
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var endExclusive)) continue;
            if (!f.TryGetProperty("value", out var v) ||
                !decimal.TryParse(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            // xBRL-JSON periods end at midnight of the next day.
            (grossLine ? grossLines : facts).Add(new Fact(concept, DateOnly.FromDateTime(start), DateOnly.FromDateTime(endExclusive).AddDays(-1),
                dims.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "", value, statutory));
        }

        // A statutory-column figure only where the report has no plain one for the same line and period.
        var plain = facts.Where(f => !f.Statutory).Select(f => (f.Concept, f.Start, f.End, f.Unit)).ToHashSet();
        facts = facts.Where(f => !f.Statutory || !plain.Contains((f.Concept, f.Start, f.End, f.Unit))).ToList();

        var years = new List<EsefYear>();
        // The report's own year: the latest year-long period it tags, up to the date the index gives. The index sometimes
        // gives the filing date instead of the year end (Unilever's reports since 2022 are listed at 9 February), and a
        // year ending six weeks earlier would otherwise not be found.
        var anchor = facts.Where(f => (f.End.DayNumber - f.Start.DayNumber) is >= 340 and <= 380 && f.End <= reportPeriodEnd.AddDays(10))
            .Select(f => f.End).DefaultIfEmpty(reportPeriodEnd).Max();
        anchor = FiscalYearEnd(facts, anchor);
        // The reported year and its comparative: annual-length periods ending ~0 and ~1 year before the report's year end.
        foreach (var yearsBack in new[] { 0, 1 })
        {
            var target = anchor.AddYears(-yearsBack);
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
            // A top line can't be smaller than its own gross profit. Medicover's reports since 2023 don't tag revenue, so
            // €4.3M of interest income passed for it; cost of sales + gross profit is the revenue by definition.
            decimal? Line(string concept) => grossLines.FirstOrDefault(f => f.Concept == concept && f.Start == revenue.Start
                && f.End == revenue.End && f.Unit == revenue.Unit)?.Value;
            if (revenue.Concept != RevenueConcepts[0] && Line(GrossProfitConcept) is { } gross && gross > revenue.Value
                && Line(CostOfSalesConcept) is { } cost && cost > 0)
            {
                revenue = revenue with { Concept = RevenueConcepts[0], Value = gross + cost };
                candidates[revenue.Concept] = revenue.Value;
            }
            // A fund manager's fees are its top line (St. James's Place: £3.8bn beside £24M of insurance revenue); net of
            // the fees passed on where those are tagged (Amundi), as banks show net revenue.
            else if (SideLines.Contains(revenue.Concept) && Line(FeeIncomeConcept) is { } fees
                     && fees - Math.Abs(Line(FeeExpenseConcept) ?? 0) is var earned && earned > 4 * revenue.Value)
            {
                candidates[FeeIncomeConcept] = earned;   // for a loss-making year too, so the history keeps one measure
                // A profit bigger than "revenue" is what shows the side line isn't the top line.
                if (net.Value > revenue.Value) revenue = revenue with { Concept = FeeIncomeConcept, Value = earned };
            }
            years.Add(new EsefYear(FiscalYearOf(end), end, revenue.Value, net.Value,
                inYear.FirstOrDefault(f => f.Concept == OperatingConcept && f.Unit == revenue.Unit)?.Value,
                inYear.FirstOrDefault(f => f.Concept == EpsConcept)?.Value,
                revenue.Unit["iso4217:".Length..], revenue.Concept, candidates));
        }
        return years;
    }

    /// <summary>
    /// The end of the fiscal year, given the newest year-long period tagged. An interim report can tag the last twelve
    /// months too (DFDS's September 2026 report made a "full year 2026"): that period ends where the year to date ends,
    /// and the year to date starts the day after the real fiscal year ended.
    /// </summary>
    private static DateOnly FiscalYearEnd(List<Fact> facts, DateOnly newest)
    {
        var toDate = facts.Where(f => f.End == newest && f.End.DayNumber - f.Start.DayNumber < 340).Select(f => f.Start.AddDays(-1)).ToHashSet();
        return facts.Where(f => f.End < newest && f.End.DayNumber - f.Start.DayNumber is >= 340 and <= 380 && toDate.Contains(f.End))
            .Select(f => f.End).DefaultIfEmpty(newest).Max();
    }

    /// <summary>A company's own income-statement column that is the statutory total ("cna:ResultsForTheYearMember").</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@":(ResultsForThe(Year|Period)|Statutory|StatutoryResults|Reported|ReportedResults|Total|TotalResults|IFRS)Member$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex StatutoryMember();

    /// <summary>A 52/53-week year ending in the first days of January belongs to the year before.</summary>
    internal static int FiscalYearOf(DateOnly end) => end is { Month: 1, Day: <= 7 } ? end.Year - 1 : end.Year;

    public static FinancialPeriod ToPeriod(string companyId, EsefYear y, string source) => new()
    {
        CompanyId = companyId, PeriodType = PeriodType.Annual, FiscalYear = y.FiscalYear, Revenue = y.Revenue, NetIncome = y.NetIncome,
        OperatingIncome = y.OperatingIncome, Eps = y.Eps, SourceFiling = source
    };

    /// <summary>
    /// A company's comparable yearly figures from all its reports (newest wins, so restatements count), each with the
    /// report it came from. One revenue concept and one currency for every year; empty when nothing was tagged.
    /// </summary>
    public static async Task<Dictionary<int, (EsefYear Year, string Source)>> CollectYearsAsync(
        Sec.ISecClient client, UkEntity entity, string label, List<string> warnings, CancellationToken ct)
    {
        var years = new Dictionary<int, (EsefYear Year, string Source)>();
        var reports = new List<EsefReport>();
        foreach (var filing in entity.Filings.OrderByDescending(f => f.PeriodEnd))
        {
            var json = await client.GetStringAsync(filing.JsonUrl, Sec.CachePolicy.Immutable, ct);
            if (json is null) { warnings.Add($"{label}: report data for {filing.PeriodEnd} could not be downloaded"); continue; }
            try
            {
                var found = Extract(json, filing.PeriodEnd);
                if (KronerTaggedAsEuros.Contains(entity.Lei)) found = found.Select(y => y.Currency == "EUR" ? y with { Currency = "DKK" } : y).ToList();
                if (found.Count > 0)
                    reports.Add(new EsefReport(filing.PeriodEnd, filing.JsonUrl.Replace(".json", ""), found));
            }
            catch (JsonException) { warnings.Add($"{label}: report data for {filing.PeriodEnd} is not valid JSON"); }
        }
        foreach (var report in EsefReconciler.Reconcile(reports, label, warnings))
            foreach (var y in report.Years) years.TryAdd(y.FiscalYear, (y, report.Source));
        if (years.Count == 0) return years;
        var latest = years.Values.MaxBy(y => y.Year.FiscalYear).Year;
        // One revenue definition for all years (a bank can tag different lines in different reports).
        foreach (var fy in years.Keys.ToList())
            years[fy] = (years[fy].Year.WithRevenueConcept(latest.RevenueConcept), years[fy].Source);
        // A year that didn't tag that line is measured differently; mixing them fakes growth (Barclays £12bn → £35bn).
        foreach (var fy in years.Where(y => y.Value.Year.RevenueConcept != latest.RevenueConcept).Select(y => y.Key).ToList()) years.Remove(fy);
        // Other currencies in older reports (a company that switched to reporting in dollars) can't be compared; drop them.
        foreach (var fy in years.Where(y => y.Value.Year.Currency != latest.Currency).Select(y => y.Key).ToList()) years.Remove(fy);
        return years;
    }
}
