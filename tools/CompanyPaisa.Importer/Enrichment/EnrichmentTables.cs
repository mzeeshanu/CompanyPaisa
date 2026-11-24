using System.Globalization;
using System.Text;
using CompanyPaisa.Core.Domain;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>A company's website and careers page, where each came from, and when the careers page was last looked for.</summary>
public sealed record CompanySite(string CompanyId, string? Website, string? WebsiteSource, string? CareersUrl, DateOnly? CareersCheckedOn);

/// <summary>A location placed at its street address; kept only while the address it was found for is unchanged.</summary>
public sealed record GeocodedLocation(string LocationId, string Address, double Latitude, double Longitude, string Source);

/// <summary>
/// The enrichment tables in data/reference: reviewable CSVs that outlive every market import. Importers apply them when they
/// publish (so a monthly refresh keeps websites, careers pages, street positions and categories), and --enrich adds to them.
/// </summary>
public static class EnrichmentTables
{
    public static Dictionary<string, CompanySite> ReadSites(string path)
    {
        var sites = new Dictionary<string, CompanySite>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Csv.Read(path).Skip(1))
        {
            if (f.Count < 5) continue;
            sites[f[0]] = new CompanySite(f[0], Blank(f[1]), Blank(f[2]), Blank(f[3]),
                DateOnly.TryParse(f[4], CultureInfo.InvariantCulture, out var d) ? d : null);
        }
        return sites;
    }

    public static void WriteSites(string path, IEnumerable<CompanySite> sites) =>
        Csv.Write(path, ["company_id", "website", "website_source", "careers_url", "careers_checked_on"],
            sites.OrderBy(s => s.CompanyId, StringComparer.OrdinalIgnoreCase)
                .Select(s => new[] { s.CompanyId, s.Website ?? "", s.WebsiteSource ?? "", s.CareersUrl ?? "",
                    s.CareersCheckedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" }));

    public static Dictionary<string, GeocodedLocation> ReadPoints(string path)
    {
        var points = new Dictionary<string, GeocodedLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Csv.Read(path).Skip(1))
        {
            if (f.Count < 5 || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            points[f[0]] = new GeocodedLocation(f[0], f[1], lat, lng, f[4]);
        }
        return points;
    }

    public static void WritePoints(string path, IEnumerable<GeocodedLocation> points) =>
        Csv.Write(path, ["location_id", "address", "latitude", "longitude", "source"],
            points.OrderBy(p => p.LocationId, StringComparer.OrdinalIgnoreCase)
                .Select(p => new[] { p.LocationId, p.Address, p.Latitude.ToString("F6", CultureInfo.InvariantCulture),
                    p.Longitude.ToString("F6", CultureInfo.InvariantCulture), p.Source }));

    private static readonly string[] CategoryHeader = ["company_id", "scheme", "code", "industry", "name", "source", "checked_on"];

    /// <summary>data/reference/company-categories.csv: the category each company's exchange publishes (written by --enrich categories).</summary>
    public static Dictionary<string, IndustryClassification> ReadCategories(string path)
    {
        var categories = new Dictionary<string, IndustryClassification>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Csv.Read(path).Skip(1))
        {
            if (f.Count < 7 || f[1] == "" || f[4] == "") continue;
            categories[f[0]] = new IndustryClassification
            {
                Scheme = f[1], Code = Blank(f[2]), Industry = Blank(f[3]), Name = f[4], Source = f[5],
                CheckedOn = DateOnly.TryParse(f[6], CultureInfo.InvariantCulture, out var d) ? d : null
            };
        }
        return categories;
    }

    public static void WriteCategories(string path, IReadOnlyDictionary<string, IndustryClassification> categories) =>
        Csv.Write(path, CategoryHeader, categories.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new[] { p.Key, p.Value.Scheme, p.Value.Code ?? "", p.Value.Industry ?? "", p.Value.Name, p.Value.Source,
                p.Value.CheckedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "" }));

    /// <summary>
    /// data/curated/category-overrides.csv: categories set by hand after review, with the reason (company_id, scheme, code,
    /// industry, name, reason, reviewed_on). They win over the exchange's and the SEC's.
    /// </summary>
    public static Dictionary<string, IndustryClassification> ReadCategoryOverrides(string path)
    {
        var overrides = new Dictionary<string, IndustryClassification>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Csv.Read(path).Skip(1))
        {
            if (f.Count < 7 || f[1] == "" || f[4] == "") continue;
            overrides[f[0]] = new IndustryClassification
            {
                Scheme = f[1], Code = Blank(f[2]), Industry = Blank(f[3]), Name = f[4], Source = "CompanyPaisa review",
                CheckedOn = DateOnly.TryParse(f[6], CultureInfo.InvariantCulture, out var d) ? d : null
            };
        }
        return overrides;
    }

    /// <summary>A company with its category applied: the classification, its name as the industry, and the broad sector from it.</summary>
    public static Company WithCategory(Company c, IndustryClassification k) =>
        c with { Classification = k, Industry = k.Name, Sector = SectorClassifier.FromClassification(k, c.Sector) };

    /// <summary>The address a location was geocoded from: street, city, state, postcode.</summary>
    public static string AddressOf(CompanyLocation l) => $"{l.Street}, {l.City}, {l.State} {l.PostalCode}".Trim();

    /// <summary>A market's rows with the tables applied: websites filled in, careers pages added, locations at their street.</summary>
    public static (IReadOnlyList<Company> Companies, IReadOnlyList<CompanyLocation> Locations) Apply(
        IReadOnlyList<Company> companies, IReadOnlyList<CompanyLocation> locations,
        IReadOnlyDictionary<string, CompanySite> sites, IReadOnlyDictionary<string, GeocodedLocation> points,
        IReadOnlyDictionary<string, IndustryClassification>? categories = null, IReadOnlyDictionary<string, IndustryClassification>? overrides = null)
    {
        var c = companies.Select(x => sites.TryGetValue(x.CompanyId, out var s)
            ? x with { Website = x.Website ?? s.Website, CareersUrl = s.CareersUrl ?? x.CareersUrl } : x)
            .Select(x => (overrides?.GetValueOrDefault(x.CompanyId) ?? categories?.GetValueOrDefault(x.CompanyId)) is { } k ? WithCategory(x, k) : x)
            .ToList();
        var l = locations.Select(x => points.TryGetValue(x.LocationId, out var p) && p.Address == AddressOf(x)
            ? x with { Point = new GeoPoint(p.Latitude, p.Longitude) } : x).ToList();
        return (c, l);
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Minimal CSV: commas, double-quoted fields with "" escapes, UTF-8.</summary>
public static class Csv
{
    public static List<List<string>> Read(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : [];

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    public static void Write(string path, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder();
        sb.Append(string.Join(",", header.Select(Quote))).Append('\n');
        foreach (var r in rows) sb.Append(string.Join(",", r.Select(Quote))).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string Quote(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
