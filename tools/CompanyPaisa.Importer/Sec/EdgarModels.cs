using System.Globalization;
using System.Text.Json;

namespace CompanyPaisa.Importer.Sec;

public sealed record SecAddress(string Street, string City, string State, string Zip);

/// <param name="Items">8-K item numbers, e.g. "5.02,9.01" (empty for other forms).</param>
public sealed record SecFiling(string Form, DateOnly FilingDate, string AccessionNumber, string PrimaryDocument, string Items = "")
{
    /// <summary>https://www.sec.gov/Archives/edgar/data/{cik}/{accession-no-dashes}/{document}</summary>
    public string Url(long cik) => $"https://www.sec.gov/Archives/edgar/data/{cik}/{AccessionNumber.Replace("-", "")}/{PrimaryDocument}";
}

/// <summary>What EDGAR's submissions API says about a filer.</summary>
public sealed record SecCompany(
    long Cik,
    string Name,
    IReadOnlyList<string> Tickers,
    IReadOnlyList<string> Exchanges,
    int? Sic,
    string SicDescription,
    string? FiscalYearEnd,
    string? Website,
    SecAddress? BusinessAddress,
    IReadOnlyList<SecFiling> Filings)
{
    public string Cik10 => Cik.ToString("D10", CultureInfo.InvariantCulture);
    public string? PrimaryTicker => Tickers.FirstOrDefault();
    public string? PrimaryExchange => Exchanges.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
}

public static class SubmissionsParser
{
    /// <summary>Parses data.sec.gov/submissions/CIK##########.json (the main file).</summary>
    public static (SecCompany Company, IReadOnlyList<string> OlderFilingPages) Parse(long cik, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var filings = root.TryGetProperty("filings", out var f) ? f : default;
        var recent = filings.ValueKind == JsonValueKind.Object && filings.TryGetProperty("recent", out var r) ? ParseFilingArrays(r) : [];
        var older = filings.ValueKind == JsonValueKind.Object && filings.TryGetProperty("files", out var files)
            ? files.EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToList()
            : [];

        SecAddress? address = null;
        if (root.TryGetProperty("addresses", out var addrs) && addrs.TryGetProperty("business", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            var street = string.Join(" ", new[] { Str(b, "street1"), Str(b, "street2") }.Where(s => !string.IsNullOrWhiteSpace(s)));
            // Foreign addresses can leave stateOrCountry empty and put EDGAR's code (A6 = Ontario) in countryCode instead.
            address = new SecAddress(street, Str(b, "city") ?? "", Str(b, "stateOrCountry") ?? Str(b, "countryCode") ?? "", (Str(b, "zipCode") ?? "").Trim());
        }

        var company = new SecCompany(
            cik,
            Str(root, "name") ?? $"CIK {cik}",
            Strings(root, "tickers"),
            Strings(root, "exchanges"),
            int.TryParse(Str(root, "sic"), out var sic) ? sic : null,
            Str(root, "sicDescription") ?? "",
            Str(root, "fiscalYearEnd"),
            Str(root, "website"),
            address,
            recent);
        return (company, older);
    }

    /// <summary>Parses an older-filings page (CIK##########-submissions-001.json): same arrays, no "recent" wrapper.</summary>
    public static IReadOnlyList<SecFiling> ParseFilingsPage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseFilingArrays(doc.RootElement);
    }

    private static List<SecFiling> ParseFilingArrays(JsonElement e)
    {
        var forms = e.GetProperty("form").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var dates = e.GetProperty("filingDate").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var acc = e.GetProperty("accessionNumber").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var docs = e.GetProperty("primaryDocument").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var items = e.TryGetProperty("items", out var it) ? it.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToList() : [];
        var list = new List<SecFiling>(forms.Count);
        for (var i = 0; i < forms.Count; i++)
            if (DateOnly.TryParse(dates[i], CultureInfo.InvariantCulture, out var d))
                list.Add(new SecFiling(forms[i], d, acc[i], docs[i], i < items.Count ? items[i] : ""));
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToList()
            : [];
}
