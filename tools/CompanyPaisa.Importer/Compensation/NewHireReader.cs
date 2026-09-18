using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Compensation;

/// <summary>
/// Officer appointments and their announced packages from a company's recent 8-Ks (Item 5.02), read by <see cref="NewHireParser"/>.
/// Linking to existing people: the company's own pay rows first, then the SEC insider list (Forms 3/4/5), then a name that
/// only one person in the whole data set has.
/// </summary>
public sealed class NewHireReader(IEdgarService edgar, IOptions<ImporterOptions> options, ILogger<NewHireReader> logger)
{
    /// <param name="knownIds">Person ids already assigned at this company, by name key (<see cref="ImportPipeline.PersonId"/>).</param>
    public async Task<List<NewExecutive>> ReadAsync(SecCompany sec, string companyId, IReadOnlyDictionary<string, string> knownIds, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-options.Value.NewHires.Months);
        var byPerson = new Dictionary<string, NewExecutive>();
        foreach (var filing in sec.Filings
                     .Where(f => f.Form is "8-K" or "8-K/A" && f.Items.Contains("5.02", StringComparison.Ordinal) && f.FilingDate >= since)
                     .OrderBy(f => f.FilingDate))
        {
            var html = await edgar.GetDocumentAsync(filing.Url(sec.Cik), ct);
            if (html is null) continue;
            IReadOnlyList<ParsedNewHire> hires;
            try { hires = NewHireParser.Parse(html, filing.FilingDate); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "{Company}: couldn't read the 8-K of {Date}", companyId, filing.FilingDate);
                continue;
            }
            foreach (var h in hires)
            {
                var row = new NewExecutive
                {
                    CompanyId = companyId, Name = h.Name, Title = Capitalised(h.Title), AnnouncedOn = filing.FilingDate, StartsOn = h.StartsOn,
                    SourceFiling = filing.Url(sec.Cik),
                    Package = h.Parts.Select(p => new PackageItem(Enum.Parse<PackageItemKind>(p.Kind.ToString()), p.Amount, p.Label)).ToList()
                };
                // The same appointment is sometimes announced twice (an 8-K, then an amendment with the agreement's terms):
                // keep the fuller one, dated by the first announcement.
                var key = ImportPipeline.PersonId(h.Name);
                if (byPerson.TryGetValue(key, out var earlier))
                    row = row.Package.Count >= earlier.Package.Count ? row with { AnnouncedOn = earlier.AnnouncedOn } : earlier;
                byPerson[key] = row;
            }
        }
        if (byPerson.Count == 0) return [];

        // Link: pay rows at this company, else the insider list (the new officer files a Form 3 when they start).
        var insiders = byPerson.Keys.Any(k => !knownIds.ContainsKey(k)) ? await edgar.GetInsidersAsync(sec.Cik, ct) : [];
        return byPerson.Select(kv => kv.Value with
        {
            PersonId = knownIds.GetValueOrDefault(kv.Key)
                       ?? (InsiderMatcher.Match(kv.Value.Name, insiders) is { } match ? $"{kv.Key}-{match.Cik}" : null)
        }).ToList();
    }

    /// <summary>"senior vice president and Chief Financial Officer" → "Senior Vice President and Chief Financial Officer".</summary>
    public static string Capitalised(string title) => char.IsLower(title[0]) ? Geo.Text.TitleCase(title) : title;

    /// <summary>
    /// Last resort for people still unlinked: a name that exactly one person in the data set has ("terrence-moorehead-1754431").
    /// A common name shared by two people links nobody.
    /// </summary>
    public static List<NewExecutive> LinkByUniqueName(IEnumerable<NewExecutive> rows, IEnumerable<string> personIds)
    {
        var byKey = personIds.GroupBy(id => id[..Math.Max(0, id.LastIndexOf('-'))], StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return rows.Select(r => r.PersonId is not null ? r
            : byKey.TryGetValue(ImportPipeline.PersonId(r.Name), out var id) ? r with { PersonId = id } : r).ToList();
    }
}
