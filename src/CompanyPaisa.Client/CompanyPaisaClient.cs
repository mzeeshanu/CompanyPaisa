using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompanyPaisa.Contracts;

namespace CompanyPaisa.Client;

/// <summary>Typed access to the CompanyPaisa API v1 for other applications.</summary>
public interface ICompanyPaisaClient
{
    /// <summary>Companies near a ZIP/city (<c>Near</c>) or coordinates. Unset values use the server defaults.</summary>
    Task<NearbyCompaniesResponse> GetCompaniesNearAsync(NearbyCompaniesRequest request, CancellationToken ct = default);

    /// <summary>Shortcut: companies near a ZIP code.</summary>
    Task<NearbyCompaniesResponse> GetCompaniesNearZipAsync(string zip, double? radiusMiles = null, CancellationToken ct = default);

    /// <summary>Company profile, or null if the ticker is unknown.</summary>
    Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken ct = default);

    Task<FinancialsResponse?> GetFinancialsAsync(string ticker, PeriodType period = PeriodType.Quarterly, int? years = null, CancellationToken ct = default);
    Task<ExecutivesResponse?> GetExecutivesAsync(string ticker, int? years = null, CancellationToken ct = default);

    /// <summary>Named executives of public companies near a ZIP/city or coordinates, with their pay history.</summary>
    Task<ExecutivesNearResponse> GetExecutivesNearAsync(ExecutivesNearRequest request, CancellationToken ct = default);

    /// <summary>One person's career and pay across companies, or null if unknown.</summary>
    Task<ExecutiveDetailDto?> GetExecutiveAsync(string personId, CancellationToken ct = default);

    /// <summary>ZIP code or "City, ST" → coordinates, or null if not found.</summary>
    Task<GeoLookupDto?> LookupAsync(string query, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default);
    Task<DataMetaDto> GetMetaAsync(CancellationToken ct = default);
}

/// <summary>Thrown for non-success responses other than 404-on-lookup. Carries the API's problem details.</summary>
public sealed class CompanyPaisaApiException(HttpStatusCode status, string? title, string? detail, string body)
    : Exception($"CompanyPaisa API returned {(int)status} {status}: {title ?? detail ?? body}")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? Title { get; } = title;
    public string? Detail { get; } = detail;
    public string ResponseBody { get; } = body;
}

public sealed class CompanyPaisaClient(HttpClient http) : ICompanyPaisaClient
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<NearbyCompaniesResponse> GetCompaniesNearAsync(NearbyCompaniesRequest r, CancellationToken ct = default) =>
        GetRequiredAsync<NearbyCompaniesResponse>("api/v1/companies/near" + Query(
            ("near", r.Near), ("latitude", Num(r.Latitude)), ("longitude", Num(r.Longitude)), ("radiusMiles", Num(r.RadiusMiles)), ("region", r.Region),
            ("sector", r.Sector), ("headquarteredOnly", r.HeadquarteredOnly ? "true" : null), ("sort", r.Sort?.ToString()),
            ("reverse", r.Reverse ? "true" : null), ("search", r.Search), ("includeBubbles", r.IncludeBubbles ? "true" : null),
            ("page", r.Page?.ToString(CultureInfo.InvariantCulture)), ("pageSize", r.PageSize?.ToString(CultureInfo.InvariantCulture))), ct);

    public Task<NearbyCompaniesResponse> GetCompaniesNearZipAsync(string zip, double? radiusMiles = null, CancellationToken ct = default) =>
        GetCompaniesNearAsync(new NearbyCompaniesRequest { Near = zip, RadiusMiles = radiusMiles }, ct);

    /// <summary>Every company in a country or state: a code ("US", "US-TX", "CA-ON", "UK") or a name ("Texas").</summary>
    public Task<NearbyCompaniesResponse> GetCompaniesInRegionAsync(string region, CancellationToken ct = default) =>
        GetCompaniesNearAsync(new NearbyCompaniesRequest { Region = region, PageSize = 5000 }, ct);

    public Task<CompanyDetailDto?> GetCompanyAsync(string ticker, CancellationToken ct = default) =>
        GetOptionalAsync<CompanyDetailDto>($"api/v1/companies/{Uri.EscapeDataString(ticker)}", ct);

    public Task<FinancialsResponse?> GetFinancialsAsync(string ticker, PeriodType period = PeriodType.Quarterly, int? years = null, CancellationToken ct = default) =>
        GetOptionalAsync<FinancialsResponse>($"api/v1/companies/{Uri.EscapeDataString(ticker)}/financials" +
            Query(("period", period.ToString()), ("years", years?.ToString(CultureInfo.InvariantCulture))), ct);

    public Task<ExecutivesResponse?> GetExecutivesAsync(string ticker, int? years = null, CancellationToken ct = default) =>
        GetOptionalAsync<ExecutivesResponse>($"api/v1/companies/{Uri.EscapeDataString(ticker)}/executives" +
            Query(("years", years?.ToString(CultureInfo.InvariantCulture))), ct);

    /// <summary>Facts worked out from the company's figures and similar companies nearby; null for an unknown ticker.</summary>
    public Task<CompanyInsightsResponse?> GetCompanyInsightsAsync(string ticker, CancellationToken ct = default) =>
        GetOptionalAsync<CompanyInsightsResponse>($"api/v1/companies/{Uri.EscapeDataString(ticker)}/insights", ct);

    /// <summary>Salaries the company offered by job title (its US H-1B wage filings); null for an unknown ticker.</summary>
    public Task<JobSalariesResponse?> GetJobSalariesAsync(string ticker, CancellationToken ct = default) =>
        GetOptionalAsync<JobSalariesResponse>($"api/v1/companies/{Uri.EscapeDataString(ticker)}/salaries", ct);

    public Task<ExecutivesNearResponse> GetExecutivesNearAsync(ExecutivesNearRequest r, CancellationToken ct = default) =>
        GetRequiredAsync<ExecutivesNearResponse>("api/v1/executives/near" + Query(
            ("near", r.Near), ("latitude", Num(r.Latitude)), ("longitude", Num(r.Longitude)), ("radiusMiles", Num(r.RadiusMiles)), ("region", r.Region),
            ("sector", r.Sector), ("includeFormer", r.IncludeFormer ? "true" : null), ("search", r.Search), ("role", r.Role?.ToString()), ("sort", r.Sort?.ToString()),
            ("years", r.Years?.ToString(CultureInfo.InvariantCulture)),
            ("page", r.Page?.ToString(CultureInfo.InvariantCulture)), ("pageSize", r.PageSize?.ToString(CultureInfo.InvariantCulture))), ct);

    public Task<ExecutiveDetailDto?> GetExecutiveAsync(string personId, CancellationToken ct = default) =>
        GetOptionalAsync<ExecutiveDetailDto>($"api/v1/executives/{Uri.EscapeDataString(personId)}", ct);

    /// <summary>Companies (by name or ticker) and executives (by name), best matches first.</summary>
    public Task<NameSearchResponse> SearchByNameAsync(string query, int? limit = null, CancellationToken ct = default) =>
        GetRequiredAsync<NameSearchResponse>("api/v1/search" + Query(("q", query), ("limit", limit?.ToString(CultureInfo.InvariantCulture))), ct);

    public Task<GeoLookupDto?> LookupAsync(string query, CancellationToken ct = default) =>
        GetOptionalAsync<GeoLookupDto>("api/v1/geo/lookup" + Query(("q", query)), ct);

    public Task<IReadOnlyList<string>> GetSectorsAsync(CancellationToken ct = default) =>
        GetRequiredAsync<IReadOnlyList<string>>("api/v1/sectors", ct);

    public Task<DataMetaDto> GetMetaAsync(CancellationToken ct = default) =>
        GetRequiredAsync<DataMetaDto>("api/v1/meta", ct);

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken ct) =>
        await GetOptionalAsync<T>(path, ct, notFoundIsNull: false)
        ?? throw new CompanyPaisaApiException(HttpStatusCode.NoContent, "Empty response", null, "");

    private async Task<T?> GetOptionalAsync<T>(string path, CancellationToken ct, bool notFoundIsNull = true)
    {
        using var response = await http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull) return default;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            string? title = null, detail = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
                detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
            }
            catch (JsonException) { /* not a problem-details body */ }
            throw new CompanyPaisaApiException(response.StatusCode, title, detail, body);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static string? Num(double? v) => v?.ToString(CultureInfo.InvariantCulture);

    private static string Query(params (string Key, string? Value)[] pairs)
    {
        var parts = pairs.Where(p => !string.IsNullOrEmpty(p.Value))
                         .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}")
                         .ToList();
        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }
}
