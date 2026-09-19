using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Enrichment;

/// <summary>One address to place: the location, its country and where it sits now (the postcode's centre).</summary>
public sealed record AddressToPlace(string LocationId, string Country, string Street, string City, string State, string PostalCode, GeoPoint Now);

/// <summary>
/// Places street addresses. US addresses go to the US Census Bureau's free batch geocoder (1,000 per request); the rest to
/// OpenStreetMap's Nominatim, one request a second as its usage policy asks. A result is kept only when it lands within
/// <see cref="MaxMilesFromPostcode"/> of the postcode's centre, so a wrong match can't move a company across the country.
/// </summary>
public sealed class StreetGeocoder(HttpClient http, ILogger logger)
{
    public const double MaxMilesFromPostcode = 25;
    private const string CensusUrl = "https://geocoding.geo.census.gov/geocoder/locations/addressbatch";
    private const string NominatimUrl = "https://nominatim.openstreetmap.org/search";
    private readonly HaversineDistanceCalculator _distance = new();

    public async Task<List<(AddressToPlace Address, GeoPoint Point, string Source)>> PlaceAsync(IReadOnlyList<AddressToPlace> addresses, CancellationToken ct)
    {
        var found = new List<(AddressToPlace, GeoPoint, string)>();
        var us = addresses.Where(a => a.Country == "US").ToList();
        for (var i = 0; i < us.Count; i += 1000)
        {
            found.AddRange(await CensusAsync(us.Skip(i).Take(1000).ToList(), ct));
            logger.LogInformation("US Census geocoder: {Done}/{Total} addresses sent, {Found} placed so far", Math.Min(i + 1000, us.Count), us.Count, found.Count);
        }
        var others = addresses.Where(a => a.Country != "US").ToList();
        var n = 0;
        foreach (var a in others)
        {
            if (await NominatimAsync(a, ct) is { } p) found.Add((a, p, "openstreetmap"));
            if (++n % 100 == 0) logger.LogInformation("OpenStreetMap: {Done}/{Total} addresses, {Found} placed so far", n, others.Count, found.Count);
            await Task.Delay(1100, ct);   // Nominatim: at most one request a second
        }
        return found;
    }

    private async Task<List<(AddressToPlace, GeoPoint, string)>> CensusAsync(List<AddressToPlace> batch, CancellationToken ct)
    {
        var csv = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
            csv.Append(i).Append(',').Append(Field(batch[i].Street)).Append(',').Append(Field(batch[i].City)).Append(',')
               .Append(Field(batch[i].State)).Append(',').Append(Field(batch[i].PostalCode)).Append('\n');
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "addressFile", "addresses.csv");
        form.Add(new StringContent("Public_AR_Current"), "benchmark");
        string body;
        try
        {
            using var response = await http.PostAsync(CensusUrl, form, ct);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("US Census geocoder failed for a batch ({Message}); those addresses stay at their postcode", ex.Message);
            return [];
        }
        // "id","input address","Match","Exact","matched address","lon,lat","tiger id","side"
        var found = new List<(AddressToPlace, GeoPoint, string)>();
        foreach (var row in Csv.Parse(body))
        {
            if (row.Count < 6 || row[2] != "Match" || !int.TryParse(row[0], out var id) || id >= batch.Count) continue;
            var lonLat = row[5].Split(',');
            if (lonLat.Length != 2 || !double.TryParse(lonLat[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                !double.TryParse(lonLat[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            var point = new GeoPoint(lat, lon);
            if (Near(batch[id], point)) found.Add((batch[id], point, "us-census"));
        }
        return found;
    }

    private async Task<GeoPoint?> NominatimAsync(AddressToPlace a, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["format"] = "jsonv2", ["limit"] = "1", ["street"] = a.Street, ["city"] = a.City, ["countrycodes"] = a.Country.ToLowerInvariant()
        };
        if (a.PostalCode.Length > 0) query["postalcode"] = a.PostalCode;
        var url = NominatimUrl + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", CareersFinder.UserAgent);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object) return null;
            var point = new GeoPoint(double.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture));
            return Near(a, point) ? point : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private bool Near(AddressToPlace a, GeoPoint p) => p.IsValid && _distance.DistanceMiles(a.Now, p) <= MaxMilesFromPostcode;

    private static string Field(string s) => s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

}
