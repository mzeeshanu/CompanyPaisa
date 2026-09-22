using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using CompanyPaisa.Importer.Enrichment;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Importer.Postings;

/// <summary>One US job ad: its id on the board, title, the places it names, a link, and its pay range (yearly USD) if stated.</summary>
public sealed record Posting(string Id, string Title, string Location, string Url, PayRange? Pay);

/// <summary>
/// Reads a board's open jobs through the hiring system's public job feed — the same data the company's careers page
/// shows. Greenhouse, Lever and Ashby return every job with its text in one call; SmartRecruiters and Workday list
/// jobs first and give each ad's text on its own page, so those are read once per job and remembered (<c>known</c>).
/// Requests to one host are spaced out; only US jobs are kept.
/// </summary>
public sealed class BoardReader(HttpClient http, ILogger logger, int maxNewDetailsPerBoard)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The public job-board APIs of Greenhouse, Lever, Ashby and SmartRecruiters serve every careers-page widget on the
    /// web: a request every 100 ms is light. Workday's job sites are each company's own, so they get more room.
    /// </summary>
    private static TimeSpan Spacing(string host) =>
        host.EndsWith("myworkdayjobs.com", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromMilliseconds(350) : TimeSpan.FromMilliseconds(100);

    /// <param name="known">A job already read in an earlier run (by id), so its ad isn't fetched again.</param>
    public async Task<List<Posting>> ReadAsync(JobBoard board, Func<string, Posting?> known, CancellationToken ct) => board.System switch
    {
        JobBoard.Greenhouse => await GreenhouseAsync(board.Board, ct),
        JobBoard.Lever => await LeverAsync(board.Board, ct),
        JobBoard.Ashby => await AshbyAsync(board.Board, ct),
        JobBoard.SmartRecruiters => await SmartRecruitersAsync(board.Board, known, ct),
        JobBoard.Workday => await WorkdayAsync(board, known, ct),
        _ => []
    };

    /// <summary>
    /// Whether a board exists, what it calls itself (Greenhouse gives a name) and, for Lever and Ashby, which don't, the
    /// text of a few of its ads (to see whose they are).
    /// </summary>
    public async Task<(bool Exists, string? Name, string Text)> ProbeAsync(JobBoard board, CancellationToken ct)
    {
        switch (board.System)
        {
            case JobBoard.Greenhouse:
                using (var doc = await JsonAsync(HttpMethod.Get, $"https://boards-api.greenhouse.io/v1/boards/{board.Board}", null, ct))
                    return doc is not null && Str(doc.RootElement, "name") is { } name ? (true, name, "") : (false, null, "");
            case JobBoard.Lever:
                using (var doc = await JsonAsync(HttpMethod.Get, $"https://api.lever.co/v0/postings/{board.Board}?mode=json&limit=3", null, ct))
                    return doc?.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                        ? (true, null, doc.RootElement.GetRawText()) : (false, null, "");
            case JobBoard.Ashby:
                using (var doc = await JsonAsync(HttpMethod.Get, $"https://api.ashbyhq.com/posting-api/job-board/{board.Board}", null, ct))
                    return doc is not null && doc.RootElement.TryGetProperty("jobs", out var jobs) && jobs.GetArrayLength() > 0
                        ? (true, null, string.Join(" ", jobs.EnumerateArray().Take(3).Select(j => j.GetRawText()))) : (false, null, "");
            default:
                return (false, null, "");
        }
    }

    // ---- Greenhouse ------------------------------------------------------------------------------------------------

    private async Task<List<Posting>> GreenhouseAsync(string token, CancellationToken ct)
    {
        using var doc = await JsonAsync(HttpMethod.Get, $"https://boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true", null, ct);
        var list = new List<Posting>();
        if (doc is null || !doc.RootElement.TryGetProperty("jobs", out var jobs)) return list;
        foreach (var j in jobs.EnumerateArray())
        {
            var location = j.TryGetProperty("location", out var l) ? Str(l, "name") ?? "" : "";
            if (!UsPlaces.IsUs(location)) continue;
            list.Add(new Posting(j.GetProperty("id").ToString(), Str(j, "title") ?? "", location, Str(j, "absolute_url") ?? "", PayText.Read(Str(j, "content"))));
        }
        return list;
    }

    // ---- Lever -----------------------------------------------------------------------------------------------------

    private async Task<List<Posting>> LeverAsync(string token, CancellationToken ct)
    {
        using var doc = await JsonAsync(HttpMethod.Get, $"https://api.lever.co/v0/postings/{token}?mode=json", null, ct);
        var list = new List<Posting>();
        if (doc?.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            var cat = j.TryGetProperty("categories", out var c) ? c : default;
            var location = cat.ValueKind == JsonValueKind.Object
                ? string.Join("; ", (cat.TryGetProperty("allLocations", out var all) && all.ValueKind == JsonValueKind.Array
                    ? all.EnumerateArray().Select(x => x.GetString()) : [Str(cat, "location")]).OfType<string>())
                : "";
            var country = Str(j, "country");
            if (!(country is "US" || country is null && UsPlaces.IsUs(location))) continue;
            PayRange? pay = null;
            if (j.TryGetProperty("salaryRange", out var s) && s.ValueKind == JsonValueKind.Object && Str(s, "currency") is "USD" &&
                s.TryGetProperty("min", out var min) && s.TryGetProperty("max", out var max) && min.TryGetDecimal(out var lo) && max.TryGetDecimal(out var hi))
            {
                var per = Str(s, "interval") ?? "";
                pay = PayText.Read($"${lo:0.##} - ${hi:0.##} {(per.Contains("hour") ? "per hour" : per.Contains("month") ? "per month" : "per year")}");
            }
            pay ??= PayText.Read(string.Join(" ", Str(j, "salaryDescriptionPlain"), Str(j, "additionalPlain"), Str(j, "descriptionPlain"), ListsText(j)));
            list.Add(new Posting(Str(j, "id") ?? "", Str(j, "text") ?? "", location, Str(j, "hostedUrl") ?? "", pay));
        }
        return list;
    }

    private static string ListsText(JsonElement j) => j.TryGetProperty("lists", out var lists) && lists.ValueKind == JsonValueKind.Array
        ? string.Join(" ", lists.EnumerateArray().Select(x => Str(x, "content"))) : "";

    // ---- Ashby -----------------------------------------------------------------------------------------------------

    private async Task<List<Posting>> AshbyAsync(string token, CancellationToken ct)
    {
        using var doc = await JsonAsync(HttpMethod.Get, $"https://api.ashbyhq.com/posting-api/job-board/{token}?includeCompensation=true", null, ct);
        var list = new List<Posting>();
        if (doc is null || !doc.RootElement.TryGetProperty("jobs", out var jobs)) return list;
        foreach (var j in jobs.EnumerateArray())
        {
            if (j.TryGetProperty("isListed", out var listed) && listed.ValueKind == JsonValueKind.False) continue;
            var places = new List<string?> { Str(j, "location") };
            if (j.TryGetProperty("secondaryLocations", out var more) && more.ValueKind == JsonValueKind.Array)
                places.AddRange(more.EnumerateArray().Select(x => Str(x, "location")));
            var location = string.Join("; ", places.OfType<string>());
            var country = j.TryGetProperty("address", out var a) && a.TryGetProperty("postalAddress", out var pa) ? Str(pa, "addressCountry") : null;
            if (!(country is "USA" or "United States" or "US" || UsPlaces.IsUs(location))) continue;
            var comp = j.TryGetProperty("compensation", out var cp) && cp.ValueKind == JsonValueKind.Object
                ? Str(cp, "scrapeableCompensationSalarySummary") ?? Str(cp, "compensationTierSummary") : null;
            var pay = PayText.Read(comp) ?? PayText.Read(Str(j, "descriptionHtml"));
            list.Add(new Posting(Str(j, "id") ?? "", (Str(j, "title") ?? "").Trim(), location, Str(j, "jobUrl") ?? "", pay));
        }
        return list;
    }

    // ---- SmartRecruiters -------------------------------------------------------------------------------------------

    private async Task<List<Posting>> SmartRecruitersAsync(string company, Func<string, Posting?> known, CancellationToken ct)
    {
        var list = new List<Posting>();
        var fetched = 0;
        for (var offset = 0; offset < 2000; offset += 100)
        {
            using var doc = await JsonAsync(HttpMethod.Get, $"https://api.smartrecruiters.com/v1/companies/{company}/postings?limit=100&offset={offset}", null, ct);
            if (doc is null || !doc.RootElement.TryGetProperty("content", out var content) || content.GetArrayLength() == 0) break;
            foreach (var j in content.EnumerateArray())
            {
                var loc = j.TryGetProperty("location", out var l) ? l : default;
                if (Str(loc, "country") is not "us") continue;
                var id = Str(j, "id") ?? "";
                var location = string.Join(", ", new[] { Str(loc, "city"), Str(loc, "region") }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (known(id) is { } seen) { list.Add(seen); continue; }
                if (fetched >= maxNewDetailsPerBoard) continue;
                fetched++;
                using var detail = await JsonAsync(HttpMethod.Get, $"https://api.smartrecruiters.com/v1/companies/{company}/postings/{id}", null, ct);
                var text = detail is not null && detail.RootElement.TryGetProperty("jobAd", out var ad) ? ad.ToString() : "";
                var url = detail is not null ? Str(detail.RootElement, "postingUrl") ?? Str(detail.RootElement, "applyUrl") : null;
                list.Add(new Posting(id, Str(j, "name") ?? "", location, url ?? $"https://jobs.smartrecruiters.com/{company}/{id}", PayText.Read(text)));
            }
            if (content.GetArrayLength() < 100) break;
        }
        return list;
    }

    // ---- Workday ---------------------------------------------------------------------------------------------------

    private async Task<List<Posting>> WorkdayAsync(JobBoard board, Func<string, Posting?> known, CancellationToken ct)
    {
        var (tenant, site) = (board.Board.Split('/')[0], board.Board.Split('/')[1]);
        var api = $"https://{board.Host}/wday/cxs/{tenant}/{site}";
        var list = new List<Posting>();
        var fetched = 0;
        for (var offset = 0; offset < 2000; offset += 20)
        {
            using var doc = await JsonAsync(HttpMethod.Post, $"{api}/jobs", $$"""{"appliedFacets":{},"limit":20,"offset":{{offset}},"searchText":""}""", ct);
            if (doc is null || !doc.RootElement.TryGetProperty("jobPostings", out var jobs) || jobs.GetArrayLength() == 0) break;
            foreach (var j in jobs.EnumerateArray())
            {
                var path = Str(j, "externalPath");
                if (path is null) continue;
                var locations = Str(j, "locationsText") ?? "";
                // "2 Locations" says nothing yet; anything else must be in the US.
                if (!UsPlaces.IsUs(locations) && !locations.Contains("Locations", StringComparison.OrdinalIgnoreCase)) continue;
                if (known(path) is { } seen) { list.Add(seen); continue; }
                if (fetched >= maxNewDetailsPerBoard) continue;
                fetched++;
                using var detail = await JsonAsync(HttpMethod.Get, api + path, null, ct);
                if (detail is null || !detail.RootElement.TryGetProperty("jobPostingInfo", out var info)) continue;
                var places = new List<string?> { Str(info, "location") };
                if (info.TryGetProperty("additionalLocations", out var more) && more.ValueKind == JsonValueKind.Array)
                    places.AddRange(more.EnumerateArray().Select(x => x.GetString()));
                var location = string.Join("; ", places.OfType<string>());
                var country = info.TryGetProperty("country", out var c) ? Str(c, "descriptor") : null;
                if (country is not null && country != "United States of America" && !UsPlaces.IsUs(location)) continue;
                list.Add(new Posting(path, Str(info, "title") ?? Str(j, "title") ?? "", location,
                    Str(info, "externalUrl") ?? $"https://{board.Host}/{site}{path}", PayText.Read(Str(info, "jobDescription"))));
            }
            if (jobs.GetArrayLength() < 20) break;
        }
        return list;
    }

    // ---- HTTP ------------------------------------------------------------------------------------------------------

    private async Task<JsonDocument?> JsonAsync(HttpMethod method, string url, string? body, CancellationToken ct)
    {
        var host = new Uri(url).Host;
        var gate = _hosts.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await gate.WaitAsync(ct);
            try
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.TryAddWithoutValidation("User-Agent", CareersFinder.UserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    logger.LogWarning("{Status} from {Host}; waiting", (int)response.StatusCode, host);
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt), ct);
                    continue;
                }
                if (!response.IsSuccessStatusCode) return null;
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                return JsonDocument.Parse(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (attempt == 3) return null;
            }
            finally
            {
                await Task.Delay(Spacing(host), CancellationToken.None);
                gate.Release();
            }
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
