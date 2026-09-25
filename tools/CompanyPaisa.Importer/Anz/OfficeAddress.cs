using System.Text.RegularExpressions;
using CompanyPaisa.Importer.Pk;

namespace CompanyPaisa.Importer.Anz;

/// <param name="Street">The address before the town, as printed ("Level 10, 1 Martin Place").</param>
public sealed record OfficeAddressText(string Street, string City, string Postcode);

/// <summary>
/// The company's own office from its annual report's corporate directory: the address printed under "Registered office",
/// "Principal place of business" or "Head office" — never the share registry's or the auditor's, which sit nearby.
/// Australian addresses end "SYDNEY NSW 2000"; New Zealand ones "Auckland 1010".
/// </summary>
public static partial class OfficeAddress
{
    public static OfficeAddressText? Find(IReadOnlyList<PdfTextLine> pdfLines, string country)
    {
        var lines = pdfLines.Select(l => AnnualReportReader.Clean(l.Text)).ToList();
        var pattern = country == "NZ" ? NzTown() : AuTown();
        // Labels first (principal place of business is where the company is run; the registered office can be an accountant's).
        foreach (var label in new[] { PrincipalLabel(), OfficeLabel() })
            for (var i = 0; i < lines.Count; i++)
            {
                if (!label.IsMatch(lines[i]) || lines[i].Length > 140) continue;
                // The label's own line (a two-column directory) and up to five lines below, stopping at another label.
                var text = new List<string>();
                for (var j = i; j < Math.Min(lines.Count, i + 6); j++)
                {
                    if (j > i && (OtherLabel().IsMatch(lines[j]) || label.IsMatch(lines[j]))) break;
                    var part = j == i ? label.Replace(lines[j], " ") : lines[j];
                    // Two columns side by side ("Registered office  Share registry"): the left one.
                    var cells = Regex.Split(part.Trim(), @"\s{2,}");
                    text.Add(j == i || cells.Length == 1 ? part : cells[0]);
                    var joined = string.Join(", ", text.Select(t => t.Trim(' ', ',', ':')).Where(t => t.Length > 0));
                    if (pattern.Match(joined) is { Success: true } m)
                    {
                        // Only the address: from its first part with a number or a level/suite ("Shareholder enquiries,
                        // 1 Woolworths Way, Bella Vista" → "1 Woolworths Way, Bella Vista").
                        var parts = joined[..(m.Index + m.Groups["city"].Length)].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                        // The town on its own after a comma is shown separately; "Bella Vista" (a two-word suburb) stays.
                        if (parts.Count > 1 && parts[^1].Equals(m.Groups["city"].Value, StringComparison.OrdinalIgnoreCase)) parts.RemoveAt(parts.Count - 1);
                        var first = parts.FindIndex(p => p.Any(char.IsDigit) || AddressStart().IsMatch(p));
                        var street = string.Join(", ", first > 0 ? parts.Skip(first) : parts).Trim(' ', ',', ':');
                        return new OfficeAddressText(street.Length > 160 ? street[..160] : street, TitleCase(m.Groups["city"].Value.Trim()), m.Groups["pc"].Value);
                    }
                }
            }
        return null;
    }

    private static string TitleCase(string s) =>
        s.Any(char.IsLower) ? s : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

    [GeneratedRegex(@"principal\s+(place\s+of\s+business|office|administrative\s+office)|head\s+office|corporate\s+(head\s+)?office", RegexOptions.IgnoreCase)]
    private static partial Regex PrincipalLabel();
    [GeneratedRegex(@"registered\s+(office|address)", RegexOptions.IgnoreCase)] private static partial Regex OfficeLabel();
    [GeneratedRegex(@"share\s+regist|registry|auditor|solicitor|lawyer|banker|stock\s+exchange|postal\s+address|website|telephone|phone|email|securities\s+exchange", RegexOptions.IgnoreCase)]
    private static partial Regex OtherLabel();
    [GeneratedRegex(@"^(level|suite|unit|floor|ground\s+floor|po\s+box|gpo\s+box|locked\s+bag)\b", RegexOptions.IgnoreCase)] private static partial Regex AddressStart();

    /// <summary>"… Martin Place, SYDNEY NSW 2000": the word before the state (the town's full name comes from its postcode).</summary>
    [GeneratedRegex(@"(?<city>[A-Za-z][A-Za-z'.\-]*)[,\s]+(?<state>NSW|VIC|QLD|WA|SA|TAS|ACT|NT|New South Wales|Victoria|Queensland|Western Australia|South Australia|Tasmania)[,\s]+(?<pc>\d{4})\b")]
    private static partial Regex AuTown();
    [GeneratedRegex(@"(?<city>Auckland|Wellington|Christchurch|Hamilton|Tauranga|Dunedin|Napier|Nelson|Palmerston North|New Plymouth|Queenstown|Rotorua|Whangārei|Whangarei|Invercargill|Hastings|Lower Hutt|Petone|Porirua|Timaru|Blenheim|Gisborne|Whanganui|Masterton|Upper Hutt|Mount Maunganui|Cambridge|Te Awamutu|Ashburton|Wanaka|Takapuna|Newmarket|Parnell|Penrose|Mangere|Manukau|Albany|Rolleston|Wigram)[,\s]+(?<pc>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex NzTown();
}
