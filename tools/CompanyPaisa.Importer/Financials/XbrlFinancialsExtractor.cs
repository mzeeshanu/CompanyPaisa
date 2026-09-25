using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Financials;

/// <summary>Turns SEC XBRL "company facts" into annual and quarterly revenue / net income.</summary>
public interface IFinancialsExtractor
{
    FinancialsResult Extract(string companyId, long cik, string companyFactsJson, int years);
}

public sealed record FinancialsResult(IReadOnlyList<FinancialPeriod> Periods, string? RevenueConcept, IReadOnlyList<string> Notes)
{
    public decimal? LatestAnnualRevenue => Periods.Where(p => p.PeriodType == PeriodType.Annual).OrderBy(p => p.FiscalYear).LastOrDefault()?.Revenue;
    public DateOnly? LatestPeriodEnd { get; init; }
    /// <summary>Currency the figures are in (ISO code) — Canadian companies often report in CAD.</summary>
    public string Currency { get; init; } = "USD";
}

public sealed partial class XbrlFinancialsExtractor : IFinancialsExtractor
{
    // Revenue candidates; per period the largest one tagged wins (companies switch concepts over the years, ASC 606 etc.).
    private static readonly string[] RevenueConcepts =
    [
        "Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax", "RevenueFromContractWithCustomerIncludingAssessedTax",
        "SalesRevenueNet", "SalesRevenueGoodsNet", "SalesRevenueServicesNet", "RevenuesNetOfInterestExpense",
        "RealEstateRevenueNet", "OperatingLeasesIncomeStatementLeaseRevenue", "RevenueMineralSales", "ElectricUtilityRevenue"
    ];
    private static readonly string[] NetIncomeConcepts =
    [
        "NetIncomeLoss", "ProfitLoss", "NetIncomeLossAvailableToCommonStockholdersBasic", "IncomeLossFromContinuingOperations"
    ];
    // IFRS (Canadian 40-F filers and other foreign companies that file XBRL with the SEC).
    private static readonly string[] IfrsRevenueConcepts =
    [
        "Revenue", "RevenueFromContractsWithCustomers", "RevenueFromSaleOfGoods", "RevenueFromRenderingOfServices", "RevenueAndOperatingIncome"
    ];
    private static readonly string[] IfrsNetIncomeConcepts = ["ProfitLoss", "ProfitLossAttributableToOwnersOfParent"];

    internal sealed record Fact(DateOnly? Start, DateOnly End, decimal Value, string? Frame, string Accession);

    public FinancialsResult Extract(string companyId, long cik, string json, int years)
    {
        var notes = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("facts", out var facts))
            return new FinancialsResult([], null, ["No XBRL facts."]);

        // US GAAP or IFRS, whichever has the newest revenue: a company that switched standards (Agnico Eagle, Alcon)
        // keeps its old US GAAP facts forever, and those would freeze its figures in the year it switched.
        JsonElement gaap = default;
        string[] revenueConcepts = RevenueConcepts, netIncomeConcepts = NetIncomeConcepts;
        var hasGaap = facts.TryGetProperty("us-gaap", out var usGaap);
        var hasIfrs = facts.TryGetProperty("ifrs-full", out var ifrs);
        if (hasIfrs && (!hasGaap || LatestEnd(ifrs, IfrsRevenueConcepts) > LatestEnd(usGaap, RevenueConcepts)))
            (gaap, revenueConcepts, netIncomeConcepts) = (ifrs, IfrsRevenueConcepts, IfrsNetIncomeConcepts);
        else if (hasGaap)
            gaap = usGaap;
        else
            return new FinancialsResult([], null, ["No us-gaap or IFRS XBRL facts."]);

        // The reporting currency: US dollars when the company reports in them, else the currency most of its revenue facts use.
        var currency = ReportingCurrency(gaap, revenueConcepts);
        if (currency != "USD") notes.Add($"Reports in {currency}.");

        // Per period, the largest revenue line tagged: a total is never smaller than its own sub-lines, and companies also
        // tag segment or product lines without dimensions (Acadia Healthcare's 2018 "Revenues" was $1.9bn of $3.0bn).
        // Banks: interest & dividend income + non-interest income is a candidate too.
        const string BankRevenue = "InterestAndDividendIncomeOperating + NoninterestIncome";
        var candidates = revenueConcepts.Select(c => (c, (IEnumerable<Fact>)Facts(gaap, c, currency)))
            .Append((BankRevenue, SumByFrame(Facts(gaap, "InterestAndDividendIncomeOperating", currency), Facts(gaap, "NoninterestIncome", currency)).Values));
        var (revenue, revenueConcept) = LargestByFrame(candidates);
        if (revenueConcept == BankRevenue) notes.Add("Revenue = interest & dividend income + non-interest income (bank).");
        var (netIncome, _) = MergeByFrame(gaap, netIncomeConcepts, currency);
        if (revenue.Count == 0) return new FinancialsResult([], null, ["No revenue facts in XBRL."]);

        // ---- annual: frames "CY2023" (≈12-month duration) ----
        var annual = revenue.Where(kv => AnnualFrame().IsMatch(kv.Key))
            .Select(kv => (Rev: kv.Value, Ni: netIncome.GetValueOrDefault(kv.Key)))
            .Where(x => x.Ni is not null)
            .Select(x => new FinancialPeriod
            {
                CompanyId = companyId,
                PeriodType = PeriodType.Annual,
                FiscalYear = FiscalYearLabel(x.Rev.End),
                Revenue = x.Rev.Value,
                NetIncome = x.Ni!.Value,
                SourceFiling = FilingUrl(cik, x.Rev.Accession)
            })
            .GroupBy(p => p.FiscalYear).Select(g => g.Last())
            .OrderBy(p => p.FiscalYear).ToList();

        // ---- quarterly: frames "CY2023Q2"; derive the missing fiscal Q4 = annual − other three ----
        var quarters = new Dictionary<(int Y, int Q), (decimal Rev, decimal Ni, string Acc)>();
        foreach (var (frame, f) in revenue)
        {
            var m = QuarterFrame().Match(frame);
            if (!m.Success || netIncome.GetValueOrDefault(frame) is not { } ni) continue;
            quarters[(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))] = (f.Value, ni.Value, f.Accession);
        }
        var derived = 0;
        foreach (var (frame, a) in revenue.Where(kv => AnnualFrame().IsMatch(kv.Key)))
        {
            if (a.Start is null || netIncome.GetValueOrDefault(frame) is not { } ani) continue;
            var inYear = CalendarQuartersIn(a.Start.Value, a.End).ToList();
            var missing = inYear.Where(q => !quarters.ContainsKey(q)).ToList();
            if (inYear.Count != 4 || missing.Count != 1) continue;
            var others = inYear.Where(q => q != missing[0]).Select(q => quarters[q]).ToList();
            // A negative remainder means the year and its quarters were measured differently; leave that quarter out.
            if (a.Value - others.Sum(o => o.Rev) < 0) continue;
            quarters[missing[0]] = (a.Value - others.Sum(o => o.Rev), ani.Value - others.Sum(o => o.Ni), a.Accession);
            derived++;
        }
        if (derived > 0) notes.Add($"{derived} fourth quarter(s) derived as annual minus the other three quarters.");

        // A year restated later (a business sold, an accounting change) no longer matches the quarters as first reported.
        // Keep the restated year and drop those quarters, so a chart never mixes the two bases.
        var restated = 0;
        foreach (var (frame, a) in revenue.Where(kv => AnnualFrame().IsMatch(kv.Key)))
        {
            if (a.Start is null) continue;
            var inYear = CalendarQuartersIn(a.Start.Value, a.End).ToList();
            if (inYear.Count != 4 || !inYear.All(quarters.ContainsKey)) continue;
            var sum = inYear.Sum(q => quarters[q].Rev);
            if (Math.Abs(sum - a.Value) <= 0.02m * Math.Abs(a.Value)) continue;
            foreach (var q in inYear) quarters.Remove(q);
            restated++;
        }
        if (restated > 0) notes.Add($"{restated} year(s) restated after their quarters were reported; those quarters left out.");

        var latestYear = annual.Count > 0 ? annual[^1].FiscalYear : DateTime.UtcNow.Year;
        var fromYear = latestYear - years + 1;
        var quarterly = quarters
            .Where(kv => kv.Key.Y >= fromYear - 1)
            .OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.Q)
            .Select(kv => new FinancialPeriod
            {
                CompanyId = companyId,
                PeriodType = PeriodType.Quarterly,
                FiscalYear = kv.Key.Y,
                FiscalQuarter = kv.Key.Q,
                Revenue = kv.Value.Rev,
                NetIncome = kv.Value.Ni,
                SourceFiling = FilingUrl(cik, kv.Value.Acc)
            }).ToList();

        var periods = annual.Where(p => p.FiscalYear >= fromYear).Concat(quarterly).ToList();
        var latestEnd = revenue.Values.Max(f => f.End);
        return new FinancialsResult(periods, revenueConcept, notes) { LatestPeriodEnd = latestEnd, Currency = currency };
    }

    /// <summary>Retail-style years ending in January/February belong to the previous fiscal year.</summary>
    private static int FiscalYearLabel(DateOnly end) => end.Month <= 2 ? end.Year - 1 : end.Year;

    /// <summary>Calendar quarters whose end date falls inside (start, end], allowing a few days of slack.</summary>
    private static IEnumerable<(int Y, int Q)> CalendarQuartersIn(DateOnly start, DateOnly end)
    {
        for (var d = new DateOnly(start.Year, (start.Month - 1) / 3 * 3 + 1, 1); d <= end.AddDays(20); d = d.AddMonths(3))
        {
            var qEnd = d.AddMonths(3).AddDays(-1);
            if (qEnd > start.AddDays(20) && qEnd <= end.AddDays(20)) yield return (qEnd.Year, (qEnd.Month - 1) / 3 + 1);
        }
    }

    private static (Dictionary<string, Fact> ByFrame, string? Concept) MergeByFrame(JsonElement gaap, IEnumerable<string> concepts, string currency)
    {
        var result = new Dictionary<string, Fact>();
        string? first = null;
        foreach (var concept in concepts)
        {
            var any = false;
            foreach (var f in Facts(gaap, concept, currency).Where(f => f.Frame is not null))
                if (result.TryAdd(f.Frame!, f)) any = true;
            if (any) first ??= concept;
        }
        return (result, first);
    }

    /// <summary>The largest value per frame across all candidates; the concept is the one chosen most often.</summary>
    private static (Dictionary<string, Fact> ByFrame, string? Concept) LargestByFrame(IEnumerable<(string Concept, IEnumerable<Fact> Facts)> candidates)
    {
        var best = new Dictionary<string, (Fact Fact, string Concept)>();
        foreach (var (concept, facts) in candidates)
            foreach (var f in facts.Where(f => f.Frame is not null))
                if (!best.TryGetValue(f.Frame!, out var b) || f.Value > b.Fact.Value) best[f.Frame!] = (f, concept);
        var concept1 = best.Values.GroupBy(b => b.Concept).MaxBy(g => g.Count())?.Key;
        return (best.ToDictionary(kv => kv.Key, kv => kv.Value.Fact), concept1);
    }

    /// <summary>End date of the newest fact for any of the concepts, in any currency.</summary>
    private static DateOnly LatestEnd(JsonElement taxonomy, IEnumerable<string> concepts)
    {
        var latest = DateOnly.MinValue;
        foreach (var concept in concepts)
            if (taxonomy.TryGetProperty(concept, out var c) && c.TryGetProperty("units", out var units))
                foreach (var unit in units.EnumerateObject())
                    foreach (var f in unit.Value.EnumerateArray())
                        if (f.TryGetProperty("end", out var e) && DateOnly.TryParse(e.GetString(), CultureInfo.InvariantCulture, out var end) && end > latest)
                            latest = end;
        return latest;
    }

    private static Dictionary<string, Fact> SumByFrame(IEnumerable<Fact> a, IEnumerable<Fact> b)
    {
        var bByFrame = b.Where(f => f.Frame is not null).GroupBy(f => f.Frame!).ToDictionary(g => g.Key, g => g.First());
        return a.Where(f => f.Frame is not null && bByFrame.ContainsKey(f.Frame))
            .GroupBy(f => f.Frame!)
            .ToDictionary(g => g.Key, g => g.First() with { Value = g.First().Value + bByFrame[g.Key].Value });
    }

    /// <summary>USD if any revenue fact is in dollars; otherwise the currency unit with the most revenue facts (e.g. CAD).</summary>
    private static string ReportingCurrency(JsonElement gaap, IEnumerable<string> concepts)
    {
        var counts = new Dictionary<string, int>();
        foreach (var concept in concepts)
            if (gaap.TryGetProperty(concept, out var c) && c.TryGetProperty("units", out var units))
                foreach (var unit in units.EnumerateObject().Where(u => u.Name.Length == 3 && u.Name.All(char.IsUpper)))
                    counts[unit.Name] = counts.GetValueOrDefault(unit.Name) + unit.Value.GetArrayLength();
        return counts.ContainsKey("USD") || counts.Count == 0 ? "USD" : counts.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>
    /// A concept's facts in one currency, each with the SEC's calendar frame ("CY2023", "CY2023Q2"). Facts from proxy
    /// statements are never used: their pay-versus-performance tables tag the year's net income too, often without the
    /// "in thousands" scale — Con Edison's 2023 proxy gave its $2.5 billion profit as $2,519,000 — and the SEC's frame then
    /// points at that proxy fact. The frame goes to the same period's figure from a report (10-K, 10-Q, 20-F, 40-F…).
    /// </summary>
    internal static IEnumerable<Fact> Facts(JsonElement gaap, string concept, string currency = "USD")
    {
        if (!gaap.TryGetProperty(concept, out var c) || !c.TryGetProperty("units", out var units) || !units.TryGetProperty(currency, out var usd))
            yield break;
        var all = new List<(Fact Fact, string Form, string Filed)>();
        foreach (var f in usd.EnumerateArray())
        {
            if (!f.TryGetProperty("end", out var endEl) || !DateOnly.TryParse(endEl.GetString(), CultureInfo.InvariantCulture, out var end)) continue;
            DateOnly? start = f.TryGetProperty("start", out var s) && DateOnly.TryParse(s.GetString(), CultureInfo.InvariantCulture, out var sd) ? sd : null;
            var frame = f.TryGetProperty("frame", out var fr) ? fr.GetString() : null;
            var form = f.TryGetProperty("form", out var fo) ? fo.GetString() ?? "" : "";
            var filed = f.TryGetProperty("filed", out var fi) ? fi.GetString() ?? "" : "";
            all.Add((new Fact(start, end, f.GetProperty("val").GetDecimal(), frame, f.TryGetProperty("accn", out var acc) ? acc.GetString() ?? "" : ""), form, filed));
        }
        foreach (var (fact, form, _) in all)
        {
            if (!IsProxy(form)) { yield return fact; continue; }
            if (fact.Frame is null) continue;
            // The frame moves to the newest report that states the same period.
            var report = all.Where(x => !IsProxy(x.Form) && x.Fact.Start == fact.Start && x.Fact.End == fact.End)
                .OrderByDescending(x => x.Filed, StringComparer.Ordinal).Select(x => x.Fact).FirstOrDefault();
            if (report is not null) yield return report with { Frame = fact.Frame };
        }

        static bool IsProxy(string form) => form.StartsWith("DEF 14", StringComparison.Ordinal) || form.StartsWith("DEFA14", StringComparison.Ordinal)
                                            || form.StartsWith("PRE 14", StringComparison.Ordinal) || form.StartsWith("DEFM14", StringComparison.Ordinal);
    }

    private static string? FilingUrl(long cik, string accession) =>
        string.IsNullOrEmpty(accession) ? null : $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", "")}/";

    [GeneratedRegex(@"^CY(\d{4})$")] private static partial Regex AnnualFrame();
    [GeneratedRegex(@"^CY(\d{4})Q([1-4])$")] private static partial Regex QuarterFrame();
}
