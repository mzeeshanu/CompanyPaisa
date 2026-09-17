using System.Text.RegularExpressions;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Core.Services;

/// <summary>Reads roles out of executive titles as filings write them ("EVP, Chief Financial Officer and Treasurer", "President &amp; CEO").</summary>
public static partial class ExecutiveRoles
{
    /// <summary>
    /// Whether the title holds the role now. A title can hold several ("Chief Operating Officer and Chief Financial Officer");
    /// "Former", "Deputy", "Assistant" and similar titles don't count. <see cref="ExecutiveRole.Other"/> = the title names none of the
    /// roles at all (so a "Former Chief Financial Officer" is in neither CFO nor Other).
    /// </summary>
    public static bool Holds(string title, ExecutiveRole role) => role == ExecutiveRole.Other
        ? !Named.Any(r => Pattern(r).IsMatch(title))
        : !NotCurrent().IsMatch(title) && Pattern(role).IsMatch(title);

    private static readonly ExecutiveRole[] Named = [ExecutiveRole.Ceo, ExecutiveRole.Cfo, ExecutiveRole.Coo, ExecutiveRole.Technology, ExecutiveRole.Legal];

    private static Regex Pattern(ExecutiveRole role) => role switch
    {
        ExecutiveRole.Ceo => Ceo(),
        ExecutiveRole.Cfo => Cfo(),
        ExecutiveRole.Coo => Coo(),
        ExecutiveRole.Technology => Technology(),
        ExecutiveRole.Legal => Legal(),
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    [GeneratedRegex(@"chief\s+executive|\bCEO\b", RegexOptions.IgnoreCase)]
    private static partial Regex Ceo();

    [GeneratedRegex(@"chief\s+financial|\bCFO\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cfo();

    [GeneratedRegex(@"chief\s+operating|\bCOO\b", RegexOptions.IgnoreCase)]
    private static partial Regex Coo();

    [GeneratedRegex(@"chief\s+(technology|information|digital|data)\s+officer|\b(CTO|CIO|CDO)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Technology();

    [GeneratedRegex(@"general\s+counsel|chief\s+legal|\bCLO\b", RegexOptions.IgnoreCase)]
    private static partial Regex Legal();

    [GeneratedRegex(@"\b(former|deputy|assistant|associate|retired|outgoing|incoming)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotCurrent();
}
