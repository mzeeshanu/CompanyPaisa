namespace CompanyPaisa.Importer.Uk;

/// <summary>What one filing gave: its reported year and the comparative.</summary>
/// <param name="PeriodEnd">The filing's period end in the index.</param>
public sealed record EsefReport(DateOnly PeriodEnd, string Source, IReadOnlyList<EsefYear> Years)
{
    /// <summary>An annual report's newest year ends with the report (the index sometimes gives the filing date, weeks later).</summary>
    public bool IsAnnual => Years.Count > 0 && Math.Abs(Years.Max(y => y.PeriodEnd).DayNumber - PeriodEnd.DayNumber) <= 60;
}

/// <summary>
/// Repairs tagging slips in ESEF reports using the company's other reports as the evidence. Every year appears in two
/// or more reports, so a report tagged in thousands as if units (SP Group's 2026 interim: revenue 2,948,100) is exactly
/// 1,000× away from the annual report's figure for the same year. Nothing is rescaled without such a match; a figure
/// that is shown to be wrong but can't be repaired is left out.
/// </summary>
internal static class EsefReconciler
{
    private static readonly decimal[] UnitSlips = [1_000m, 1_000_000m, 0.001m, 0.000001m];
    private const decimal SameFigure = 0.002m;   // rounding between a report in millions and one in thousands
    private const decimal Absurd = 300m;

    /// <param name="reports">Newest first.</param>
    /// <returns>The reports in the same order, repaired.</returns>
    public static List<EsefReport> Reconcile(IReadOnlyList<EsefReport> reports, string label, List<string> warnings)
    {
        var kept = new List<EsefReport>();
        foreach (var report in reports)
        {
            // Revenue is part of "revenue and operating income", so it can't be many times bigger (Risanamento 2023: €596M
            // of €57M). Some companies put that tag on other income (GVS), so it takes a second sign: revenue also
            // hundreds of times the company's usual.
            var possible = report.Years.Where(y => !(y.RevenueConcept == "ifrs-full:Revenue"
                && y.RevenueCandidates.TryGetValue("ifrs-full:RevenueAndOperatingIncome", out var both) && y.Revenue > 5 * both
                && UsualRevenue(reports, y) is { } usual && y.Revenue > Absurd * usual)).ToList();
            if (possible.Count < report.Years.Count)
                warnings.Add($"{label}: report {report.PeriodEnd}: left out a revenue far bigger than the report's revenue and operating income");
            if (possible.Count > 0) kept.Add(report with { Years = possible });
        }

        // The same year and profit, with revenue hundreds of times smaller and no unit slip to explain it: the revenue tag
        // is on the wrong line (Brunello Cucinelli's 2025 report: €188,000 beside 2024's €1.28bn).
        var misTagged = kept.Where(small => kept.Any(big => !ReferenceEquals(small, big) && Overlaps(small, big).Any(p =>
            p.Mine.RevenueConcept == "ifrs-full:Revenue" && p.Other.RevenueConcept == p.Mine.RevenueConcept
            && p.Mine.Revenue * Absurd < p.Other.Revenue && Slip(p.Other.Revenue, p.Mine.Revenue) is null
            && p.Other.NetIncome != 0 && Math.Abs(p.Mine.NetIncome - p.Other.NetIncome) <= 0.1m * Math.Abs(p.Other.NetIncome)))).ToList();
        foreach (var r in misTagged)
            warnings.Add($"{label}: report {r.PeriodEnd}: revenue tag contradicted by another report of the same year; report left out");
        kept.RemoveAll(misTagged.Contains);
        if (kept.Count < 2) return kept;
        kept = FixRevenueScale(kept, label, warnings);
        kept = FixSingleFigures(kept, label, warnings);

        var anchor = Anchor(kept);
        var order = kept.Where(r => r.IsAnnual).Concat(kept.Where(r => !r.IsAnnual)).Where(r => !ReferenceEquals(r, anchor)).Prepend(anchor);
        var trusted = new List<EsefYear>();
        var repaired = new Dictionary<EsefReport, EsefReport?>(ReferenceEqualityComparer.Instance);
        foreach (var report in order)
        {
            var result = trusted.Count == 0 ? report : Repair(report, trusted, label, warnings);
            repaired[report] = result;
            if (result is null) continue;
            // The first report to state a year is the reference for it.
            foreach (var y in result.Years)
                if (!trusted.Any(t => SamePeriod(t, y))) trusted.Add(y);
        }
        return kept.Select(r => repaired[r]).OfType<EsefReport>().ToList();
    }

    /// <summary>
    /// The report whose units the others are measured against: the newest annual report, or the one before it when the
    /// newest is the one with the slip (an implausible size where the previous one's is plausible).
    /// </summary>
    private static EsefReport Anchor(List<EsefReport> reports)
    {
        var annual = reports.Where(r => r.IsAnnual).ToList();
        if (annual.Count == 0) return reports[0];
        foreach (var previous in annual.Skip(1))
        {
            if (Overlaps(annual[0], previous).Any(p => Slip(p.Other.Revenue, p.Mine.Revenue) is not null
                                                       && !Plausible(p.Mine.Revenue) && Plausible(p.Other.Revenue)))
                return previous;
        }
        return annual[0];

        static bool Plausible(decimal revenue) => revenue is >= 10_000_000m and <= 500_000_000_000m;
    }

    private static EsefReport? Repair(EsefReport report, List<EsefYear> trusted, string label, List<string> warnings)
    {
        var pairs = report.Years.SelectMany(mine => trusted.Where(t => SamePeriod(t, mine)).Select(other => (Mine: mine, Other: other))).ToList();
        if (pairs.Count == 0)
        {
            // Nothing to prove a slip with: if both figures are hundreds of times off the nearest year, don't publish them.
            var mine = report.Years[0];
            var nearest = trusted.Where(t => t.Currency == mine.Currency).MinBy(t => Math.Abs(t.PeriodEnd.DayNumber - mine.PeriodEnd.DayNumber));
            if (nearest is null || !FarApart(mine.Revenue, nearest.Revenue) || !FarApart(mine.NetIncome, nearest.NetIncome)) return report;
            warnings.Add($"{label}: report {report.PeriodEnd}: revenue and profit both over {Absurd}x away from the neighbouring year, and no other report states the year; left out");
            return null;
        }

        var revenue = pairs.SelectMany(p => p.Mine.RevenueCandidates.Values.Append(p.Mine.Revenue)
            .SelectMany(m => p.Other.RevenueCandidates.Values.Append(p.Other.Revenue).Select(o => Slip(o, m)))).FirstOrDefault(f => f is not null);
        var profit = pairs.Select(p => Slip(p.Other.NetIncome, p.Mine.NetIncome)).FirstOrDefault(f => f is not null);
        // A restated figure doesn't match exactly; it takes the other measure's slip when it is off by about as much.
        if (revenue is null && profit is { } pf && pairs.Any(p => Roughly(p.Other.Revenue, p.Mine.Revenue, pf))) revenue = pf;
        if (profit is null && revenue is { } rf && pairs.Any(p => Roughly(p.Other.NetIncome, p.Mine.NetIncome, rf))) profit = rf;
        if (revenue is null && profit is null) return report;
        // One line off while the other agrees exactly means the units agree: one mistyped fact, and which of the two is
        // mistyped can't be told from two reports. Never scale a whole report on it.
        if (revenue is null && pairs.Any(p => Same(p.Other.Revenue, p.Mine.Revenue))) return report;
        if (profit is null && pairs.Any(p => Same(p.Other.NetIncome, p.Mine.NetIncome))) return report;

        var operating = pairs.Select(p => p is { Mine.OperatingIncome: { } m, Other.OperatingIncome: { } o } ? Slip(o, m) : null).FirstOrDefault(f => f is not null);
        if (operating is null && revenue == profit) operating = revenue;
        warnings.Add($"{label}: report {report.PeriodEnd}: unit slip proved by another report of the same year; revenue ×{revenue ?? 1m:0.######}, profit ×{profit ?? 1m:0.######}");
        return report with
        {
            Years = report.Years.Select(y => y with
            {
                Revenue = y.Revenue * (revenue ?? 1m),
                RevenueCandidates = y.RevenueCandidates.ToDictionary(c => c.Key, c => c.Value * (revenue ?? 1m)),
                NetIncome = y.NetIncome * (profit ?? 1m),
                OperatingIncome = operating is { } of ? y.OperatingIncome * of : null,
            }).ToList()
        };
    }

    /// <summary>
    /// One mistyped figure in a report whose units are right (Park Street's 2023 report: 2022 profit 54,980 beside a
    /// revenue that matches): corrected when at least two reports with that same other figure outvote it.
    /// </summary>
    private static List<EsefReport> FixSingleFigures(List<EsefReport> reports, string label, List<string> warnings)
    {
        decimal? Outvoted(EsefReport report, EsefYear year, Func<EsefYear, decimal> figure, Func<EsefYear, decimal> check)
        {
            var others = reports.Where(r => !ReferenceEquals(r, report)).SelectMany(r => r.Years)
                .Where(o => SamePeriod(o, year) && Same(check(o), check(year))).ToList();
            var slipped = others.Where(o => Slip(figure(o), figure(year)) is not null).ToList();
            var agree = others.Count(o => Same(figure(o), figure(year))) + 1;
            return slipped.Count >= 2 && slipped.Count > agree ? figure(slipped[0]) : null;
        }

        return reports.Select(report => report with
        {
            Years = report.Years.Select(year =>
            {
                var revenue = Outvoted(report, year, y => y.Revenue, y => y.NetIncome);
                var profit = Outvoted(report, year, y => y.NetIncome, y => y.Revenue);
                if (revenue is null && profit is null) return year;
                warnings.Add($"{label}: report {report.PeriodEnd}: one mistyped {(revenue is null ? "profit" : "revenue")} for {year.FiscalYear}, outvoted by the company's other reports; corrected");
                return year with
                {
                    Revenue = revenue ?? year.Revenue,
                    NetIncome = profit ?? year.NetIncome,
                    RevenueCandidates = revenue is { } r
                        ? year.RevenueCandidates.ToDictionary(c => c.Key, c => c.Key == year.RevenueConcept ? r : c.Value)
                        : year.RevenueCandidates,
                };
            }).ToList()
        }).ToList();
    }

    private static readonly decimal[] TenFold = [10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m];

    /// <summary>
    /// A report whose revenue lines carry the wrong scale while its profit is right (New Wave's 2022 report: 2021 revenue
    /// SEK 67,186M where the 2021 report says 6,718.6M, the same profit in both): two reports state the same year and
    /// profit with revenues exactly a power of ten apart. The one out of line with the company's other years is the slip,
    /// and only its revenue is rescaled.
    /// </summary>
    private static List<EsefReport> FixRevenueScale(List<EsefReport> reports, string label, List<string> warnings)
    {
        var result = reports.ToList();
        for (var i = 0; i < result.Count; i++)
        {
            var report = result[i];
            var factor = result.Where(o => !ReferenceEquals(o, report)).SelectMany(o => Overlaps(report, o))
                .Where(p => Same(p.Other.NetIncome, p.Mine.NetIncome) && p.Mine.Revenue > 0 && p.Other.Revenue > 0)
                .Select(p => TenFold.Concat(TenFold.Select(f => 1 / f)).FirstOrDefault(f => Same(p.Other.Revenue, p.Mine.Revenue * f)))
                .FirstOrDefault(f => f != 0);
            if (factor == 0) continue;
            var others = result.Where(o => !ReferenceEquals(o, report)).SelectMany(o => o.Years)
                .Where(y => y.Currency == report.Years[0].Currency && y.Revenue > 0 && !report.Years.Any(m => SamePeriod(m, y)))
                .Select(y => y.Revenue).Order().ToList();
            if (others.Count == 0) continue;
            var usual = others[others.Count / 2];
            static double Distance(decimal a, decimal b) => Math.Abs(Math.Log10((double)(a / b)));
            var mine = report.Years[0].Revenue;
            if (mine <= 0 || Distance(mine * factor, usual) >= Distance(mine, usual)) continue;
            warnings.Add($"{label}: report {report.PeriodEnd}: revenue tagged at the wrong scale (another report states the same year and profit); revenue ×{factor:0.######}");
            result[i] = report with
            {
                Years = report.Years.Select(y => y with
                {
                    Revenue = y.Revenue * factor,
                    RevenueCandidates = y.RevenueCandidates.ToDictionary(c => c.Key, c => c.Value * factor)
                }).ToList()
            };
        }
        return result;
    }

    /// <summary>The middle revenue of the company's other years.</summary>
    private static decimal? UsualRevenue(IReadOnlyList<EsefReport> reports, EsefYear year)
    {
        var others = reports.SelectMany(r => r.Years).Where(y => !SamePeriod(y, year) && y.Currency == year.Currency)
            .Select(y => y.Revenue).Order().ToList();
        return others.Count > 0 ? others[others.Count / 2] : null;
    }

    private static bool Same(decimal a, decimal b) => a != 0 && Math.Abs(a - b) <= SameFigure * Math.Abs(a);

    /// <summary>The factor that turns <paramref name="mine"/> into <paramref name="other"/>, when it is exactly a unit slip.</summary>
    private static decimal? Slip(decimal other, decimal mine)
    {
        if (other == 0 || mine == 0) return null;
        foreach (var factor in UnitSlips)
            if (Math.Abs(mine * factor - other) <= SameFigure * Math.Abs(other)) return factor;
        return null;
    }

    private static bool Roughly(decimal other, decimal mine, decimal factor) =>
        mine != 0 && other / mine is var ratio && ratio >= factor / 2 && ratio <= factor * 2;

    private static bool FarApart(decimal a, decimal b) =>
        a != 0 && b != 0 && (Math.Abs(a) > Absurd * Math.Abs(b) || Math.Abs(b) > Absurd * Math.Abs(a));

    private static bool SamePeriod(EsefYear a, EsefYear b) =>
        a.Currency == b.Currency && Math.Abs(a.PeriodEnd.DayNumber - b.PeriodEnd.DayNumber) <= 10;

    private static IEnumerable<(EsefYear Mine, EsefYear Other)> Overlaps(EsefReport mine, EsefReport other) =>
        mine.Years.SelectMany(m => other.Years.Where(o => SamePeriod(m, o)).Select(o => (m, o)));
}
