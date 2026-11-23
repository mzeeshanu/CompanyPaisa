using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Financials;

/// <summary>Turns SEC XBRL "company facts" into annual and quarterly revenue / net income.</summary>
public interface IFinancialsExtractor
{
    /// <param name="statement">The newest annual report's own top line (EdgarTools), used where it can be proved right.</param>
    FinancialsResult Extract(string companyId, long cik, string companyFactsJson, int years, StatementReading? statement = null);
}

public sealed record FinancialsResult(IReadOnlyList<FinancialPeriod> Periods, string? RevenueConcept, IReadOnlyList<string> Notes)
{
    public decimal? LatestAnnualRevenue => Periods.Where(p => p.PeriodType == PeriodType.Annual).OrderBy(p => p.FiscalYear).LastOrDefault()?.Revenue;
    public DateOnly? LatestPeriodEnd { get; init; }
    /// <summary>Currency the figures are in (ISO code) — Canadian companies often report in CAD.</summary>
    public string Currency { get; init; } = "USD";
}

/// <summary>
/// Company facts give the history; the annual report's income statement (<see cref="StatementReading"/>) settles which
/// of several tagged lines is the revenue. See docs/edgartools-plan.md for why neither source is trusted alone.
/// </summary>
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

    internal sealed record Fact(DateOnly? Start, DateOnly End, decimal Value, string? Frame, string Accession, string Filed = "");

    public FinancialsResult Extract(string companyId, long cik, string json, int years, StatementReading? statement = null)
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
        // Banks: interest & dividend income + non-interest income is a candidate too, but only for periods where the bank
        // doesn't tag its own headline "net revenues" (net of interest expense): Goldman's 2025 total net revenues are
        // $58.3bn, while gross interest income plus non-interest revenues come to $125bn.
        const string BankRevenue = "InterestAndDividendIncomeOperating + NoninterestIncome";
        var netOfInterestFrames = Facts(gaap, "RevenuesNetOfInterestExpense", currency).Select(f => f.Frame).OfType<string>().ToHashSet();
        var candidates = revenueConcepts.Select(c => (c, (IEnumerable<Fact>)Facts(gaap, c, currency)))
            .Append((BankRevenue, SumByFrame(Facts(gaap, "InterestAndDividendIncomeOperating", currency), Facts(gaap, "NoninterestIncome", currency))
                .Values.Where(f => !netOfInterestFrames.Contains(f.Frame!))));
        var chosen = LargestByFrame(WithoutThresholds(candidates));
        UseLeaseIncome(gaap, currency, chosen);
        var lenderFrames = UseNetRevenueForBanks(gaap, currency, chosen, BankRevenue, notes);
        var (netIncome, _) = MergeByFrame(gaap, netIncomeConcepts, currency);
        FixProfitSigns(gaap, currency, netIncome);
        if (statement is not null) UseStatementRevenue(gaap, currency, statement, chosen, netIncome, lenderFrames, notes);
        var revenueConcept = chosen.Values.GroupBy(b => b.Concept).MaxBy(g => g.Count())?.Key;
        var revenue = chosen.ToDictionary(kv => kv.Key, kv => kv.Value.Fact);
        if (revenueConcept == BankRevenue) notes.Add("Revenue = interest & dividend income + non-interest income (bank).");
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
        // A quarterly report missing from the SEC's feed (Dow's Q2 2026) leaves the year to date: six or nine months less
        // the quarters around it. Only where the result is the size of the other quarters.
        var fromYearToDate = 0;
        foreach (var (frame, ytd) in revenue)
        {
            var m = YearToDateFrame().Match(frame);
            if (!m.Success || netIncome.GetValueOrDefault(frame) is not { } yni) continue;
            var last = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 4 + int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) - 1;
            var count = ytd.End.DayNumber - ytd.Start!.Value.DayNumber > 225 ? 3 : 2;
            var inPeriod = Enumerable.Range(last - count + 1, count).Select(i => (Y: i / 4, Q: i % 4 + 1)).ToList();
            var missing = inPeriod.Where(q => !quarters.ContainsKey(q)).ToList();
            if (missing.Count != 1) continue;
            var others = inPeriod.Where(q => q != missing[0]).Select(q => quarters[q]).ToList();
            var rest = ytd.Value - others.Sum(o => o.Rev);
            var usual = others.Average(o => o.Rev);
            if (rest < 0 || usual <= 0 || rest < usual / 3 || rest > usual * 3) continue;
            quarters[missing[0]] = (rest, yni.Value - others.Sum(o => o.Ni), ytd.Accession);
            fromYearToDate++;
        }
        if (fromYearToDate > 0) notes.Add($"{fromYearToDate} quarter(s) derived from the year to date less the other quarters.");

        var derived = 0;
        foreach (var (frame, a) in revenue.Where(kv => AnnualFrame().IsMatch(kv.Key)))
        {
            if (a.Start is null || netIncome.GetValueOrDefault(frame) is not { } ani) continue;
            var inYear = QuartersOfYear(a.End).ToList();
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
            var inYear = QuartersOfYear(a.End).ToList();
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

        var periods = annual.Where(p => p.FiscalYear >= fromYear).Concat(WithoutImpossibleQuarters(quarterly, annual.Where(p => p.FiscalYear >= fromYear).ToList())).ToList();
        var latestEnd = revenue.Values.Max(f => f.End);
        return new FinancialsResult(periods, revenueConcept, notes) { LatestPeriodEnd = latestEnd, Currency = currency };
    }

    /// <summary>Retail-style years ending in January/February belong to the previous fiscal year.</summary>
    internal static int FiscalYearLabel(DateOnly end) => end.Month <= 2 ? end.Year - 1 : end.Year;

    /// <summary>The four quarter labels of the year ending on <paramref name="end"/>.</summary>
    private static IEnumerable<(int Y, int Q)> QuartersOfYear(DateOnly end)
    {
        var (y, q) = QuarterLabel(end);
        for (var i = 3; i >= 0; i--)
        {
            var index = y * 4 + q - 1 - i;
            yield return (index / 4, index % 4 + 1);
        }
    }

    /// <summary>
    /// The calendar quarter a period ending on <paramref name="end"/> is labelled with: the one holding the day 35 days
    /// before it ends, which is the quarter a 13-week quarter mostly covers (to 31 January is Q4, to 30 April is Q1).
    /// </summary>
    internal static (int Y, int Q) QuarterLabel(DateOnly end)
    {
        var d = end.AddDays(-35);
        return (d.Year, (d.Month - 1) / 3 + 1);
    }

    /// <summary>
    /// "CY2025" for a year's duration labelled 2025, "CY2025Q3" for a quarter's, null for any other length. A quarter is
    /// numbered by its place in the fiscal year (Kroger's 16-week first quarter and the three 12-week ones are first to
    /// fourth), and the fiscal year's last quarter takes the label of the calendar quarter it ends in (see
    /// <see cref="QuarterLabel"/>), so the four labels always differ and follow on from year to year.
    /// </summary>
    /// <param name="yearEnds">The company's fiscal year-ends, oldest first.</param>
    internal static string? PeriodKey(Fact f, IReadOnlyList<DateOnly>? yearEnds = null)
    {
        if (f.Start is not { } start) return null;
        var days = f.End.DayNumber - start.DayNumber;
        if (days is >= 330 and <= 395) return string.Create(CultureInfo.InvariantCulture, $"CY{FiscalYearLabel(f.End)}");
        // The year-end before this period: a quarter's place is how many quarters of time have passed since then.
        var before = yearEnds?.LastOrDefault(e => e < f.End.AddDays(-20)) ?? default;
        var known = before != default && f.End.DayNumber - before.DayNumber <= 395;
        var place = known ? Math.Clamp((int)Math.Round((f.End.DayNumber - before.DayNumber) / 91.3), 1, 4) : 0;
        int index;
        if (known)
        {
            var (by, bq) = QuarterLabel(before);
            index = by * 4 + bq - 1 + place;
        }
        else
        {
            var (y, q) = QuarterLabel(f.End);
            index = y * 4 + q - 1;
        }
        var key = string.Create(CultureInfo.InvariantCulture, $"CY{index / 4}Q{index % 4 + 1}");
        if (days is >= 60 and <= 125) return key;
        // Six or nine months from the start of the fiscal year: "CY2026Q3T", the year to date through that quarter.
        if (known && place is 2 or 3 && days is >= 150 and <= 300 && Math.Abs(start.DayNumber - before.DayNumber - 1) <= 7) return key + "T";
        return null;
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

    /// <summary>
    /// A profit tagged with the wrong sign: the same period's consolidated profit is about the same size with the other
    /// sign, and the pre-tax result agrees with that (MBIA's 2025 10-K gave 2024's $447M loss as a $435M profit, beside a
    /// $444M consolidated loss and a $441M pre-tax loss).
    /// </summary>
    private static void FixProfitSigns(JsonElement gaap, string currency, Dictionary<string, Fact> profit)
    {
        Dictionary<string, Fact> Framed(string concept) =>
            Facts(gaap, concept, currency).Where(f => f.Frame is not null).GroupBy(f => f.Frame!).ToDictionary(g => g.Key, g => g.First());
        var pretax = Framed("IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest");
        if (pretax.Count == 0) return;
        var minority = Framed("NetIncomeLossAttributableToNoncontrollingInterest");
        foreach (var (frame, whole) in Framed("ProfitLoss"))
            if (AnnualFrame().IsMatch(frame) && profit.TryGetValue(frame, out var ours) && pretax.TryGetValue(frame, out var before)
                && Math.Sign(ours.Value) == -Math.Sign(whole.Value) && Math.Sign(before.Value) == Math.Sign(whole.Value)
                && Math.Abs(Math.Abs(ours.Value) - Math.Abs(whole.Value)) <= 0.1m * Math.Abs(whole.Value)
                && Math.Abs(Math.Abs(before.Value) - Math.Abs(whole.Value)) <= 0.3m * Math.Abs(whole.Value)
                // A big minority's share really can turn the group's profit into a loss for its own shareholders (Telos 2018).
                && (!minority.TryGetValue(frame, out var theirs) || Math.Abs(theirs.Value) <= 0.25m * Math.Abs(whole.Value)))
                profit[frame] = whole;
    }

    /// <summary>The largest value per frame across all candidates, with the concept it came from.</summary>
    private static Dictionary<string, (Fact Fact, string Concept)> LargestByFrame(IEnumerable<(string Concept, IEnumerable<Fact> Facts)> candidates)
    {
        var best = new Dictionary<string, (Fact Fact, string Concept)>();
        foreach (var (concept, facts) in candidates)
            foreach (var f in facts.Where(f => f.Frame is not null))
                if (!best.TryGetValue(f.Frame!, out var b) || f.Value > b.Fact.Value) best[f.Frame!] = (f, concept);
        return best;
    }

    /// <summary>
    /// A bank's headline revenue is net interest income + non-interest income (Bank of America 2025: $113.1bn, where gross
    /// interest income made it $191.6bn). Applies where the gross formula, or a line equal to it, would have been used.
    /// </summary>
    /// <returns>The periods in which the company reports as a lender.</returns>
    private static HashSet<string> UseNetRevenueForBanks(JsonElement gaap, string currency,
        Dictionary<string, (Fact Fact, string Concept)> chosen, string grossFormula, List<string> notes)
    {
        Dictionary<string, Fact> ByFrame(string concept) =>
            Facts(gaap, concept, currency).Where(f => f.Frame is not null).GroupBy(f => f.Frame!).ToDictionary(g => g.Key, g => g.First());

        var interest = ByFrame("InterestAndDividendIncomeOperating");
        var other = ByFrame("NoninterestIncome");
        var net = ByFrame("InterestIncomeExpenseNet");
        var expense = ByFrame("InterestExpenseOperating");
        foreach (var (frame, f) in ByFrame("InterestExpense")) expense.TryAdd(frame, f);

        var lender = other.Keys.Where(f => net.ContainsKey(f) || interest.ContainsKey(f)).ToHashSet();
        var changed = 0;
        foreach (var frame in lender)
        {
            if (!chosen.TryGetValue(frame, out var current) || !interest.TryGetValue(frame, out var income)) continue;
            var gross = income.Value + other[frame].Value;
            if (current.Concept != grossFormula && Math.Abs(current.Fact.Value - gross) > 0.01m * Math.Abs(gross)) continue;
            decimal netInterest;
            // A negative tag contradicted by income less expense is a mistyped sign (Peoples Bancorp's 2020 10-K gave 2018's
            // $129.6M net interest income as -$129.6M).
            if (net.TryGetValue(frame, out var tagged)
                && !(tagged.Value < 0 && expense.TryGetValue(frame, out var cost) && income.Value - cost.Value > 0))
                netInterest = tagged.Value;
            else if (expense.TryGetValue(frame, out var paid)) netInterest = income.Value - paid.Value;
            else continue;
            chosen[frame] = (income with { Value = netInterest + other[frame].Value }, NetBankRevenue);
            changed++;
        }
        if (changed > 0) notes.Add("Revenue = net interest income + non-interest income (bank).");
        return lender;
    }

    private const string NetBankRevenue = "InterestIncomeExpenseNet + NoninterestIncome";
    internal const string CorrectedNote = "Revenue taken from the annual report's income statement";

    /// <summary>
    /// Replaces a year's revenue with the statement's top line only when every check passes: not a lender; the same
    /// column's net income agrees with ours within 1% (so same period, currency and units); the line is labelled as the
    /// whole of revenue and no larger revenue total is printed; the SEC's company facts hold the same amount from the
    /// same report; and our figure isn't a restatement filed after that report.
    /// </summary>
    private static void UseStatementRevenue(JsonElement gaap, string currency, StatementReading statement,
        Dictionary<string, (Fact Fact, string Concept)> chosen, IReadOnlyDictionary<string, Fact> netIncome,
        IReadOnlySet<string> lenderFrames, List<string> notes)
    {
        var corrected = new HashSet<string>();
        var switches = new Dictionary<string, string>();   // rejected concept -> the statement's
        DateOnly? from = null;
        foreach (var year in statement.Years)
        {
            if (year.Revenue is not > 0 || year.NetIncome is not { } profit || profit == 0) continue;
            var revenue = year.Revenue.Value;
            if (year.Label is null || !HeadlineLabel().IsMatch(year.Label.Trim())) continue;
            if (year.LargestTotal is { } total && total > revenue * 1.01m) continue;

            var frame = chosen.FirstOrDefault(kv => AnnualFrame().IsMatch(kv.Key) && kv.Value.Fact.End == year.End).Key;
            if (frame is null || lenderFrames.Contains(frame)) continue;
            var (ours, oldConcept) = chosen[frame];
            if (Math.Abs(ours.Value - revenue) <= 0.01m * revenue) continue;
            if (!netIncome.TryGetValue(frame, out var ourProfit) || Math.Abs(ourProfit.Value - profit) > 0.01m * Math.Abs(profit)) continue;
            if (ours.Accession != statement.Accession && string.CompareOrdinal(ours.Filed, statement.Filed) > 0) continue;
            if (FindInReport(gaap, currency, statement.Accession, year.End, revenue, year.Concept) is not { } found) continue;

            chosen[frame] = (found.Fact with { Frame = frame }, found.Concept);
            corrected.Add(frame);
            if (found.Concept != oldConcept) switches[oldConcept] = found.Concept;
            if (found.Fact.Start is { } start && (from is null || start < from)) from = start;
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{CorrectedNote}: {year.End:yyyy-MM-dd} {ours.Value:N0} -> {revenue:N0}."));
        }

        // Keep the history on one measure: wherever the rejected line was used and the statement's line is tagged too,
        // use that. A recent quarter with only the rejected line is left out rather than mixed in.
        foreach (var (rejected, headline) in switches)
        {
            var tagged = Facts(gaap, headline, currency).Where(f => f.Frame is not null).GroupBy(f => f.Frame!).ToDictionary(g => g.Key, g => g.First());
            foreach (var (frame, pick) in chosen.ToList())
            {
                if (pick.Concept != rejected || corrected.Contains(frame)) continue;
                if (tagged.TryGetValue(frame, out var fact) && fact.Value > 0) chosen[frame] = (fact, headline);
                else if (QuarterFrame().IsMatch(frame) && pick.Fact.End >= from) chosen.Remove(frame);
            }
        }
    }

    /// <summary>The fact in that report with this year-end, a year's duration and this amount; the statement's concept first.</summary>
    private static (Fact Fact, string Concept)? FindInReport(JsonElement gaap, string currency, string accession, DateOnly end, decimal value, string? preferred)
    {
        var concepts = gaap.EnumerateObject().Select(p => p.Name).OrderByDescending(n => n == preferred);
        foreach (var concept in concepts)
            foreach (var f in Facts(gaap, concept, currency))
                if (f.Accession == accession && f.End == end && f.Value == value && f.Start is { } start && end.DayNumber - start.DayNumber is >= 300 and <= 400)
                    return (f, concept);
        return null;
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
    /// A concept's facts in one currency. One fact per period carries its key in <see cref="Fact.Frame"/>: "CY2023" for the
    /// fiscal year labelled 2023, "CY2023Q2" for the quarter labelled so (see <see cref="PeriodKey"/>); the rest have none.
    /// Facts from proxy statements are never used: their pay-versus-performance tables tag the year's net income too, often
    /// without the "in thousands" scale (Con Edison's 2023 proxy gave its $2.5 billion profit as $2,519,000).
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
            all.Add((new Fact(start, end, f.GetProperty("val").GetDecimal(), frame, f.TryGetProperty("accn", out var acc) ? acc.GetString() ?? "" : "", filed), form, filed));
        }
        // Every period is keyed by its own dates. The SEC's frames are calendar periods, and an off-calendar year loses
        // periods to them: Sysco's year to June 2025 is framed CY2024, so its year to June 2024 has no frame at all, and
        // Costco's 12-week Q2 and Q3 both fall nearest calendar Q1, so one of them has none.
        var reports = all.Where(x => !IsProxy(x.Form) && !AnothersStatements.Contains(x.Fact.Accession)).ToList();
        var yearEnds = reports.Where(x => x.Fact.Start is { } s && x.Fact.End.DayNumber - s.DayNumber is >= 330 and <= 395)
            .Select(x => x.Fact.End).Distinct().Order().ToList();
        var usable = reports.Select(x => (x.Fact, x.Form, x.Filed, Key: PeriodKey(x.Fact, yearEnds))).ToList();
        var picked = new HashSet<(DateOnly?, DateOnly, decimal, string, string)>();
        foreach (var group in usable.Where(x => x.Key is not null).GroupBy(x => x.Key!))
        {
            var annual = AnnualFrame().IsMatch(group.Key);
            var target = annual ? 365 : 91;
            var period = group.GroupBy(x => (x.Fact.Start, x.Fact.End))
                .OrderByDescending(p => p.Any(x => x.Fact.Frame == group.Key))     // the SEC's own pick, where it agrees
                .ThenByDescending(p => !annual || p.Any(x => !IsQuarterly(x.Form)))
                .ThenBy(p => Math.Abs(p.Key.End.DayNumber - p.Key.Start!.Value.DayNumber - target))
                .ThenByDescending(p => p.Key.End).First();
            // A whole year stated by mistake in a later 10-Q (Comfort Systems' Q1 2026 report tagged a quarter's $1.8bn as
            // 2025's revenue, over the 10-K's $9.1bn) yields to the annual report; otherwise the newest filing wins.
            var best = period.OrderByDescending(x => !annual || !IsQuarterly(x.Form))
                .ThenByDescending(x => x.Filed, StringComparer.Ordinal)
                .ThenByDescending(x => x.Fact.Frame == group.Key).First();
            picked.Add(Id(best.Fact));
        }
        foreach (var x in usable)
        {
            var frame = x.Key is null ? (x.Fact.Start is null ? x.Fact.Frame : null) : picked.Contains(Id(x.Fact)) ? x.Key : null;
            yield return InReportedUnits(x.Fact) with { Frame = frame };
        }

        static (DateOnly?, DateOnly, decimal, string, string) Id(Fact f) => (f.Start, f.End, f.Value, f.Accession, f.Filed);

        // A filing that added the "in thousands" scale to a full amount is exactly 1,000 or 1,000,000 times another of the
        // company's reports for the same period (INVE's 2023 10-K/A: a $1.62 trillion profit over the 10-K's $1.62 million).
        Fact InReportedUnits(Fact f)
        {
            if (Math.Abs(f.Value) < ScaleSlipFrom) return f;
            var right = all.Where(x => !IsProxy(x.Form) && x.Fact.Start == f.Start && x.Fact.End == f.End && x.Fact.Value != 0
                    && (Math.Abs(x.Fact.Value) * 1_000m == Math.Abs(f.Value) || Math.Abs(x.Fact.Value) * 1_000_000m == Math.Abs(f.Value)))
                .OrderByDescending(x => x.Filed, StringComparer.Ordinal).Select(x => x.Fact).FirstOrDefault();
            return right is null ? f : right with { Frame = f.Frame };
        }

        static bool IsQuarterly(string form) => form is "10-Q" or "10-Q/A";
        static bool IsProxy(string form) => form.StartsWith("DEF 14", StringComparison.Ordinal) || form.StartsWith("DEFA14", StringComparison.Ordinal)
                                            || form.StartsWith("PRE 14", StringComparison.Ordinal) || form.StartsWith("DEFM14", StringComparison.Ordinal)
                                            || form.StartsWith("DEFR14", StringComparison.Ordinal) || form.StartsWith("PRER14", StringComparison.Ordinal)   // revised
                                            || form.StartsWith("DEFC14", StringComparison.Ordinal) || form.StartsWith("PREC14", StringComparison.Ordinal);  // contested
    }

    /// <summary>Only figures this large are checked for a scale slip: no small company's real figures get near it.</summary>
    private const decimal ScaleSlipFrom = 10_000_000_000m;

    /// <summary>
    /// Filings that tag another company's statements as the filer's own, found by two companies showing identical years:
    /// Essential Utilities' 8-K of 25 March 2026 carries American Water's 2023–25.
    /// </summary>
    private static readonly HashSet<string> AnothersStatements = ["0001193125-26-124163"];

    /// <summary>The revenue ceilings of an "emerging growth company", which cover pages and notes get tagged with as revenue.</summary>
    private static readonly decimal[] GrowthCompanyCeilings = [1_070_000_000m, 1_235_000_000m];

    /// <summary>
    /// Leaves out a ceiling tagged as revenue (IonQ's "$1,235,000,000" 2022, beside $11M): exactly that amount, and over
    /// four times every other revenue figure the company gave within about a year of it.
    /// </summary>
    private static List<(string, IEnumerable<Fact>)> WithoutThresholds(IEnumerable<(string Concept, IEnumerable<Fact> Facts)> candidates)
    {
        var lists = candidates.Select(c => (c.Concept, Facts: c.Facts.ToList())).ToList();
        var others = lists.SelectMany(c => c.Facts).Where(f => !GrowthCompanyCeilings.Contains(f.Value)).ToList();
        return lists.Select(c => (c.Concept, (IEnumerable<Fact>)c.Facts.Where(f => !GrowthCompanyCeilings.Contains(f.Value)
            || others.Any(o => Math.Abs(o.End.DayNumber - f.End.DayNumber) <= 400 && o.Value * 4 >= f.Value)).ToList())).ToList();
    }

    /// <summary>
    /// A landlord that tags only its fee income as revenue: rent is the top line (Camden 2025: $1.57bn beside $13M of fees).
    /// </summary>
    private static void UseLeaseIncome(JsonElement gaap, string currency, Dictionary<string, (Fact Fact, string Concept)> chosen)
    {
        const string Lease = "OperatingLeaseLeaseIncome";
        foreach (var rent in Facts(gaap, Lease, currency).Where(f => f.Frame is not null && f.Value >= 10_000_000m))
            if (chosen.TryGetValue(rent.Frame!, out var pick) && rent.Value > 4 * pick.Fact.Value
                && rent.Value != 1_000m * pick.Fact.Value && rent.Value != 1_000_000m * pick.Fact.Value)   // the same line in the wrong units
                chosen[rent.Frame!] = (rent, Lease);
    }

    /// <summary>A quarter over $1bn and five times the company's biggest year (RPC's Q4 2015: $40.9bn) is a slip of the tag.</summary>
    private static List<FinancialPeriod> WithoutImpossibleQuarters(List<FinancialPeriod> quarters, List<FinancialPeriod> years)
    {
        if (years.Count < 2) return quarters;
        var (biggest, newest) = (years.Max(y => y.Revenue), years.Max(y => y.FiscalYear));
        return quarters.Where(q => q.FiscalYear > newest || q.Revenue <= 1_000_000_000m || q.Revenue <= 5 * biggest).ToList();
    }

    internal static string? FilingUrl(long cik, string accession) =>
        string.IsNullOrEmpty(accession) ? null : $"https://www.sec.gov/Archives/edgar/data/{cik}/{accession.Replace("-", "")}/";

    internal static bool IsHeadline(string label) => HeadlineLabel().IsMatch(label.Trim());

    // Labels that mean the whole of revenue; a sub-line ("License and other revenue", "Resident fees") never matches.
    [GeneratedRegex(@"^((total|net|operating|total net|total operating) )?(revenues?|sales)(,? net)?(,? net of (royalties|excise taxes|interest expense))?$|^revenue from tenants$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadlineLabel();
    [GeneratedRegex(@"^CY(\d{4})$")] private static partial Regex AnnualFrame();
    [GeneratedRegex(@"^CY(\d{4})Q([1-4])$")] private static partial Regex QuarterFrame();
    [GeneratedRegex(@"^CY(\d{4})Q([1-4])T$")] private static partial Regex YearToDateFrame();
}
