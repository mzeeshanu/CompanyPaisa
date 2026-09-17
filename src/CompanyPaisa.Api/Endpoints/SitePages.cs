using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Endpoints;

/// <summary>A page's title and summary, written into index.html for search engines and link previews.</summary>
public sealed record PageMeta(string Title, string Description, string CanonicalPath);

/// <summary>
/// The website's own pages (/company/AAPL, /executive/…). They are the React app like every other address, but the HTML
/// already carries the company's or person's name, so a shared link previews properly and search engines see a real title.
/// Reads the repository directly (not the tracked queries) so the HTML doesn't count as a second company view.
/// </summary>
public static partial class SitePages
{
    public static IEndpointRouteBuilder MapCompanyPaisaPages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/company/{ticker}", async (string ticker, ICompanyRepository repository, IndexHtml index, HttpContext http, CancellationToken ct) =>
            {
                var company = await repository.GetCompanyAsync(ticker, ct);
                PageMeta? meta = null;
                if (company is not null)
                {
                    var hq = (await repository.GetLocationsAsync(company.CompanyId, ct)).FirstOrDefault(l => l.IsHeadquarters);
                    var where = hq is null ? "" : $", headquartered in {hq.City}, {hq.State}";
                    meta = new PageMeta(
                        $"{company.Name} ({company.Ticker}) — revenue, profit and executive pay · CompanyPaisa",
                        $"{company.Name} ({company.Ticker}), {company.Sector}{where}: revenue, net income and executive pay over the years, from the company's filings.",
                        $"/company/{Uri.EscapeDataString(company.Ticker.ToUpperInvariant())}");
                }
                return Page(index, meta, http);
            })
            .ExcludeFromDescription();

        app.MapGet("/executive/{personId}", async (string personId, ICompanyRepository repository, IOptionsMonitor<FeatureOptions> features,
                IndexHtml index, HttpContext http, CancellationToken ct) =>
            {
                var person = features.CurrentValue.IsEnabled("Executives") ? await repository.GetPersonAsync(personId, ct) : null;
                PageMeta? meta = null;
                if (person is not null)
                {
                    var latest = (await repository.GetCompensationForPeopleAsync([person.PersonId], ct))
                        .OrderByDescending(c => c.Year).FirstOrDefault();
                    var company = latest is null ? null : (await repository.GetCompaniesAsync([latest.CompanyId], ct)).FirstOrDefault();
                    var role = latest is null || company is null ? "" : $", {latest.Title} at {company.Name}";
                    meta = new PageMeta(
                        $"{person.Name}{(company is null ? "" : $" ({company.Name})")} — pay history · CompanyPaisa",
                        $"{person.Name}{role}: salary, bonus, stock awards and total pay by year, from company filings.",
                        $"/executive/{Uri.EscapeDataString(person.PersonId)}");
                }
                return Page(index, meta, http);
            })
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>The app's HTML with this page's details; an unknown company or person still gets the app (it says so) with a 404.</summary>
    private static IResult Page(IndexHtml index, PageMeta? meta, HttpContext http)
    {
        var html = index.Read();
        if (html is null) return Results.NotFound();   // no built website (development runs it on the Vite dev server)
        http.Response.Headers.CacheControl = "no-cache";
        var origin = $"{http.Request.Scheme}://{http.Request.Host}";
        return Results.Content(meta is null ? html : WithMeta(html, meta, origin), "text/html; charset=utf-8",
            statusCode: meta is null ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
    }

    /// <summary>Replaces the title and description and adds the canonical address and Open Graph tags.</summary>
    public static string WithMeta(string html, PageMeta meta, string origin)
    {
        var enc = HtmlEncoder.Default;
        string title = enc.Encode(meta.Title), description = enc.Encode(meta.Description), url = enc.Encode(origin + meta.CanonicalPath);
        html = TitleTag().Replace(html, $"<title>{title}</title>", 1);
        html = DescriptionTag().Replace(html, "", 1);
        var head = $"""
                <meta name="description" content="{description}" />
                <link rel="canonical" href="{url}" />
                <meta property="og:site_name" content="CompanyPaisa" />
                <meta property="og:type" content="website" />
                <meta property="og:title" content="{title}" />
                <meta property="og:description" content="{description}" />
                <meta property="og:url" content="{url}" />
                <meta name="twitter:card" content="summary" />

            """;
        var at = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? html : html.Insert(at, head);
    }

    [GeneratedRegex(@"<title>.*?</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"\s*<meta\s+name=""description""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionTag();
}

/// <summary>The built website's index.html from wwwroot, re-read when a new build replaces it.</summary>
public sealed class IndexHtml(IWebHostEnvironment environment)
{
    private readonly Lock _gate = new();
    private (DateTimeOffset Modified, string Html)? _cached;

    public string? Read()
    {
        var file = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists) return null;
        lock (_gate)
        {
            if (_cached is { } c && c.Modified == file.LastModified) return c.Html;
            using var reader = new StreamReader(file.CreateReadStream());
            var html = reader.ReadToEnd();
            _cached = (file.LastModified, html);
            return html;
        }
    }
}
