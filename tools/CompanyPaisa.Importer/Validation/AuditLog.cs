using System.Globalization;
using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Enrichment;

namespace CompanyPaisa.Importer.Validation;

/// <summary>
/// One reviewed finding (data/curated/audit-decisions.csv). A person looked at what a check flagged and decided what it is,
/// so the next run doesn't ask again.
/// </summary>
/// <param name="Check">The check's name, as in the report ("Negative revenue").</param>
/// <param name="CompanyId">The company the finding is about.</param>
/// <param name="Match">Optional text the finding must also contain ("FY2022"); empty = every finding of this check for the company.</param>
/// <param name="Decision">
/// real — the figure is right (the company really reported it); source — the source itself is wrong and nothing better
/// exists; fixed — corrected in the importer or data (the finding should disappear; kept as history); override — a value
/// set by hand (category or figure); gap — a known gap that can't be filled.
/// </param>
/// <param name="Note">Why: what was checked and against what (an annual report page, the exchange's listing).</param>
/// <param name="ReviewedOn">When.</param>
public sealed record AuditDecision(string Check, string CompanyId, string Match, string Decision, string Note, DateOnly? ReviewedOn);

/// <summary>A check's findings split into those still open and those a person has already reviewed.</summary>
public sealed record AuditedCheck(CheckResult Check, IReadOnlyList<(string Finding, AuditDecision Decision)> Reviewed);

/// <summary>
/// The audit process: every validation run applies the reviewed decisions to the checks' findings. Reviewed findings stop
/// counting as warnings (they are listed with their reason instead); decisions that no longer match anything are reported
/// so the log stays true; every open finding goes to data/audit-worklist.csv, the list to work through.
/// </summary>
public static partial class AuditLog
{
    public const string DecisionsPath = "data/curated/audit-decisions.csv";
    public const string WorklistPath = "data/audit-worklist.csv";
    public static readonly string[] Decisions = ["real", "source", "fixed", "override", "gap"];
    private static readonly string[] Header = ["check", "company_id", "match", "decision", "note", "reviewed_on"];

    public static List<AuditDecision> Read(string path) =>
        Csv.Read(path).Skip(1).Where(f => f.Count >= 6 && f[0] != "" && f[1] != "")
            .Select(f => new AuditDecision(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim().ToLowerInvariant(), f[4].Trim(),
                DateOnly.TryParse(f[5], CultureInfo.InvariantCulture, out var d) ? d : null))
            .ToList();

    public static void Write(string path, IEnumerable<AuditDecision> decisions) =>
        Csv.Write(path, Header, decisions.OrderBy(d => d.Check, StringComparer.Ordinal).ThenBy(d => d.CompanyId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Match, StringComparer.Ordinal)
            .Select(d => new[] { d.Check, d.CompanyId, d.Match, d.Decision, d.Note, d.ReviewedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" }));

    /// <summary>Whether a decision covers a finding: same check, the company named in it, and the extra text if any.</summary>
    public static bool Covers(AuditDecision d, string check, string finding) =>
        d.Check.Equals(check, StringComparison.OrdinalIgnoreCase) &&
        // Ids, not names: "FMNB (Farmers National Banc Corp)" is no finding about BANC.
        Regex.IsMatch(Regex.Replace(finding, @"\([^)]*\)", " "), $@"(^|[\s,]){Regex.Escape(d.CompanyId)}($|[\s,])", RegexOptions.IgnoreCase) &&
        (d.Match == "" || finding.Contains(d.Match, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies the decisions: each check keeps only its open findings; the reviewed ones come back with their decision.</summary>
    public static (List<AuditedCheck> Checks, List<AuditDecision> Unused) Apply(IReadOnlyList<CheckResult> checks, IReadOnlyList<AuditDecision> decisions,
        int maxExamples = 12)
    {
        var used = new HashSet<AuditDecision>();
        var audited = new List<AuditedCheck>();
        foreach (var c in checks)
        {
            var open = new List<string>();
            var reviewed = new List<(string, AuditDecision)>();
            foreach (var f in c.All ?? c.Examples)
            {
                var d = decisions.FirstOrDefault(d => Covers(d, c.Name, f));
                if (d is null) { open.Add(f); continue; }
                used.Add(d);
                reviewed.Add((f, d));
            }
            audited.Add(new AuditedCheck(c with { Count = open.Count + Math.Max(0, c.Count - (c.All ?? c.Examples).Count), Examples = open.Take(maxExamples).ToList(), All = open },
                reviewed));
        }
        return (audited, decisions.Where(d => !used.Contains(d) && d.Decision != "fixed").ToList());
    }

    /// <summary>Every open finding, one row each (area, check, level, company, finding): the audit's to-do list.</summary>
    public static void WriteWorklist(string path, IEnumerable<CheckResult> checks) =>
        Csv.Write(path, ["area", "check", "level", "company_id", "finding"],
            checks.SelectMany(c => (c.All ?? c.Examples).Select(f => new[]
            {
                c.Area, c.Name, c.Severity == Severity.Error ? "error" : "warning", FirstId().Match(f).Groups[1].Value, f
            })));

    [GeneratedRegex(@"^([A-Z0-9][A-Z0-9.\-]*)")] private static partial Regex FirstId();
}
