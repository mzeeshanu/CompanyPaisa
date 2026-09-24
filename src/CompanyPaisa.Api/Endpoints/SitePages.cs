using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Xml;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Endpoints;

/// <summary>
/// A page's title and summary, written into index.html for search engines and link previews. <paramref name="Trail"/>: the
/// pages above it (name, path), for the breadcrumb search results show. <paramref name="Index"/> false: not for search results.
/// </summary>
public sealed record PageMeta(string Title, string Description, string CanonicalPath,
    IReadOnlyList<(string Name, string Path)>? Trail = null, bool Index = true);

/// <summary>
/// The website's own pages (/company/AAPL, /executive/…, /near/84043). They are the React app like every other address, but the
/// HTML already carries the page's name, so a shared link previews properly and search engines see a real title. Also the
/// sitemap and robots.txt that point search engines at those pages.
/// Reads the repository directly (not the tracked queries) so the HTML doesn't count as a second company view.
/// </summary>
public static partial class SitePages
{
    public static IEndpointRouteBuilder MapCompanyPaisaPages(this IEndpointRouteBuilder app)
    {
        // The home page (served here rather than as a static file, so it carries its content too).
        app.MapGet("/", async (PageRenderer pages, IndexHtml index, HttpContext http, CancellationToken ct) =>
                Page(index, new PageMeta("CompanyPaisa — public companies near you: revenue, profit and executive pay",
                    "See the public companies near you, how big they are, where they're heading, what their executives are paid and what their jobs pay.", "/"),
                    http, await pages.HomeAsync(ct)))
            .ExcludeFromDescription();

        app.MapGet("/company/{ticker}", async (string ticker, ICompanyRepository repository, PageRenderer pages, IndexHtml index, HttpContext http, CancellationToken ct) =>
            {
                var company = await repository.GetCompanyAsync(ticker, ct);
                PageMeta? meta = null;
                PageContent? content = null;
                if (company is not null)
                {
                    content = await pages.CompanyAsync(company, ct);
                    var hq = (await repository.GetLocationsAsync(company.CompanyId, ct)).FirstOrDefault(l => l.IsHeadquarters);
                    var where = hq is null ? "" : $", headquartered in {hq.City}, {hq.State}";
                    meta = new PageMeta(
                        $"{company.Name} ({company.Ticker}) — revenue, profit and executive pay · CompanyPaisa",
                        $"{company.Name} ({company.Ticker}), {company.Sector}{where}: revenue, net income and executive pay over the years, from the company's filings.",
                        CompanyPath(company.Ticker), [(company.Name, CompanyPath(company.Ticker))]);
                }
                return Page(index, meta, http, content);
            })
            .ExcludeFromDescription();

        // What a company pays, on its own page: what people search for ("Apple software engineer salary").
        app.MapGet("/company/{ticker}/salaries", async (string ticker, ICompanyRepository repository, PageRenderer pages, IndexHtml index,
                HttpContext http, CancellationToken ct) =>
            {
                var company = await repository.GetCompanyAsync(ticker, ct);
                var salaries = company is null ? [] : await repository.GetJobSalariesAsync(company.CompanyId, ct);
                PageMeta? meta = null;
                PageContent? content = null;
                if (company is not null && salaries.Count > 0)
                {
                    content = await pages.SalariesAsync(company, ct);
                    var titles = salaries.Count(j => j.City is null);
                    meta = new PageMeta(
                        $"{company.Name} ({company.Ticker}) salaries by job title · CompanyPaisa",
                        $"What {company.Name} pays: {titles:N0} job titles with their typical yearly salary and range, by city, from the company's US job ads and work-visa wage filings.",
                        CompanyPath(company.Ticker) + "/salaries", [(company.Name, CompanyPath(company.Ticker)), ("Salaries", CompanyPath(company.Ticker) + "/salaries")]);
                }
                return Page(index, meta, http, content);
            })
            .ExcludeFromDescription();

        app.MapGet("/executive/{personId}", async (string personId, ICompanyRepository repository, IOptionsMonitor<FeatureOptions> features,
                PageRenderer pages, IndexHtml index, HttpContext http, CancellationToken ct) =>
            {
                var person = features.CurrentValue.IsEnabled("Executives") ? await repository.GetPersonAsync(personId, ct) : null;
                PageMeta? meta = null;
                PageContent? content = null;
                if (person is not null)
                {
                    content = await pages.ExecutiveAsync(person, ct);
                    var latest = (await repository.GetCompensationForPeopleAsync([person.PersonId], ct))
                        .OrderByDescending(c => c.Year).FirstOrDefault();
                    var company = latest is null ? null : (await repository.GetCompaniesAsync([latest.CompanyId], ct)).FirstOrDefault();
                    var role = latest is null || company is null ? "" : $", {latest.Title} at {company.Name}";
                    var path = $"/executive/{Uri.EscapeDataString(person.PersonId)}";
                    meta = new PageMeta(
                        $"{person.Name}{(company is null ? "" : $" ({company.Name})")} — pay history · CompanyPaisa",
                        $"{person.Name}{role}: salary, bonus, stock awards and total pay by year, from company filings.",
                        path, company is null ? [(person.Name, path)] : [(company.Name, CompanyPath(company.Ticker)), (person.Name, path)]);
                }
                return Page(index, meta, http, content);
            })
            .ExcludeFromDescription();

        // A search: /near/84043, /near/SW1A1AA, /near/FR-75008, /near/me (the visitor's own location), + /executives.
        app.MapGet("/near/{place}/{mode:regex(^executives$)?}", async (string place, string? mode, IGeoLocator geo, PageRenderer pages, IndexHtml index,
                HttpContext http, CancellationToken ct) =>
            {
                var executives = mode is not null;
                var what = executives ? "Executives and their pay" : "Public companies";
                var path = $"/near/{Uri.EscapeDataString(place)}{(executives ? "/executives" : "")}";
                PageMeta? meta;
                PageContent? content = null;
                // Different for every visitor, and empty until the app knows where they are: not for search results.
                if (place.Equals("me", StringComparison.OrdinalIgnoreCase))
                    meta = new PageMeta($"{what} near you · CompanyPaisa",
                        "See the public companies near you, how big they are, where they're heading and what their executives are paid.", path, Index: false);
                else if (Regions.Find(place) is { } region)
                {
                    // A whole country or state: /near/texas, /near/united-kingdom, /near/US-TX.
                    var where = region.InSentence;
                    meta = new PageMeta($"{what} in {where} · CompanyPaisa",
                        executives
                            ? $"Named executives of public companies in {where}: latest pay, 10-year totals and careers, from company filings."
                            : $"Every public company in {where}: revenue, growth, profit and executive pay, from company filings.", path,
                        executives
                            ? [($"In {where}", $"/near/{Uri.EscapeDataString(place)}"), ("Executives", path)]
                            : [($"In {where}", path)]);
                    content = await pages.NearAsync(default, where, executives, ct, region);
                }
                else
                {
                    var hit = place.Length <= 20 ? await geo.LookupAsync(place, ct) : null;
                    var where = hit is null ? null : string.Join(" ", new[] { $"{hit.City}, {hit.State}", hit.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)));
                    meta = where is null ? null : new PageMeta($"{what} near {where} · CompanyPaisa",
                        executives
                            ? $"Named executives of public companies near {where}: latest pay, 10-year totals and careers, from company filings."
                            : $"Public companies near {where}: revenue, growth, profit and executive pay, from company filings.", path,
                        executives
                            ? [($"Near {where}", $"/near/{Uri.EscapeDataString(place)}"), ("Executives", path)]
                            : [($"Near {where}", path)]);
                    if (hit is not null && where is not null) content = await pages.NearAsync(hit.Point, where, executives, ct);
                }
                return Page(index, meta, http, content);
            })
            .ExcludeFromDescription();

        // The API stays open to crawlers: search engines index a page as the app draws it, and the app draws it from the API.
        // Its answers carry X-Robots-Tag: noindex instead, so they don't show up in search results themselves.
        app.MapGet("/robots.txt", (HttpContext http) =>
                Results.Text($"User-agent: *\nAllow: /\nDisallow: /admin\n\nSitemap: {Origin(http)}/sitemap.xml\n", "text/plain; charset=utf-8"))
            .ExcludeFromDescription();

        // The sitemap is an index of numbered files (/sitemap-1.xml, /sitemap-2.xml…), so it never outgrows the 50,000
        // addresses search engines read from one file.
        app.MapGet("/sitemap.xml", async (ICompanyRepository repository, IOptionsMonitor<UiOptions> ui, IOptionsMonitor<FeatureOptions> features,
                HttpContext http, CancellationToken ct) =>
            {
                var paths = await SitemapPathsAsync(repository, ui.CurrentValue, features.CurrentValue.IsEnabled("Executives"), ct);
                var lastModified = (await repository.GetMetadataAsync(ct)).AsOfDate;
                var files = (paths.Count + SitemapFileSize - 1) / SitemapFileSize;
                http.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Text(SitemapIndexXml(Origin(http), files, lastModified), "application/xml; charset=utf-8");
            })
            .ExcludeFromDescription();

        app.MapGet("/sitemap-{file:int:min(1)}.xml", async (int file, ICompanyRepository repository, IOptionsMonitor<UiOptions> ui,
                IOptionsMonitor<FeatureOptions> features, HttpContext http, CancellationToken ct) =>
            {
                var paths = await SitemapPathsAsync(repository, ui.CurrentValue, features.CurrentValue.IsEnabled("Executives"), ct);
                var page = paths.Skip((file - 1) * SitemapFileSize).Take(SitemapFileSize).ToList();
                if (page.Count == 0) return Results.NotFound();
                var lastModified = (await repository.GetMetadataAsync(ct)).AsOfDate;
                http.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.Text(SitemapXml(Origin(http), page, lastModified), "application/xml; charset=utf-8");
            })
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Addresses per sitemap file. Search engines allow 50,000; smaller files are quicker to build and to fetch, and Search
    /// Console reports each one separately.
    /// </summary>
    public const int SitemapFileSize = 10_000;

    /// <summary>The home page, every covered area, every country and state with companies, every company and every executive.</summary>
    public static async Task<IReadOnlyList<string>> SitemapPathsAsync(ICompanyRepository repository, UiOptions ui, bool executives, CancellationToken ct)
    {
        var companies = await repository.GetCompaniesAsync(ct);
        var paths = new List<string> { "/" };
        paths.AddRange(ui.Coverage.Select(a => $"/near/{Uri.EscapeDataString(PlaceToken(a.ExampleZip, a.Country))}").Distinct());
        // Every country, state and province that has a company: /near/texas, /near/united-kingdom.
        var locations = await repository.GetLocationsWithinAsync(new GeoBoundingBox(-90, 90, -180, 180), ct);
        paths.AddRange(Regions.All.Where(r => locations.Any(r.Contains)).Select(r => $"/near/{r.Slug}"));
        foreach (var c in companies)
        {
            var path = $"/company/{Uri.EscapeDataString(c.Ticker.ToUpperInvariant())}";
            paths.Add(path);
            if ((await repository.GetJobSalariesAsync(c.CompanyId, ct)).Count > 0) paths.Add(path + "/salaries");
        }
        if (executives)
            paths.AddRange((await repository.GetExecutiveCompensationAsync(companies.Select(c => c.CompanyId), ct))
                .Select(p => p.PersonId).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
                .Select(id => $"/executive/{Uri.EscapeDataString(id)}"));
        return paths;
    }

    /// <summary>
    /// How a postcode appears in a search address (the website builds the same): no spaces, and European, Australian,
    /// New Zealand and Pakistani codes carry their country ("FR-75008"), because a bare 5-digit code reads as a US ZIP.
    /// </summary>
    public static string PlaceToken(string postcode, string country)
    {
        var code = postcode.Replace(" ", "").ToUpperInvariant();
        return country is "FR" or "NL" or "IT" or "ES" or "AU" or "NZ" or "PK" ? $"{country}-{code}" : code;
    }

    private static string SitemapXml(string origin, IEnumerable<string> paths, DateOnly? lastModified)
    {
        var sb = new StringBuilder();
        using (var xml = XmlWriter.Create(sb, new XmlWriterSettings { Indent = false, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var path in paths)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", origin + path);
                if (lastModified is { } d) xml.WriteElementString("lastmod", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    private static string SitemapIndexXml(string origin, int files, DateOnly? lastModified)
    {
        var sb = new StringBuilder();
        using (var xml = XmlWriter.Create(sb, new XmlWriterSettings { Indent = false, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("sitemapindex", "http://www.sitemaps.org/schemas/sitemap/0.9");
            for (var file = 1; file <= files; file++)
            {
                xml.WriteStartElement("sitemap");
                xml.WriteElementString("loc", $"{origin}/sitemap-{file}.xml");
                if (lastModified is { } d) xml.WriteElementString("lastmod", d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    private static string Origin(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

    /// <summary>The pages this class renders on the server (for the hourly page limit).</summary>
    public static bool IsRenderedPage(string path) =>
        path == "/" || path.StartsWith("/company/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/executive/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/near/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The app's HTML with this page's details and content; an unknown company or person still gets the app (it says so)
    /// with a 404.
    /// </summary>
    private static IResult Page(IndexHtml index, PageMeta? meta, HttpContext http, PageContent? content = null)
    {
        var html = index.Read();
        if (html is null) return Results.NotFound();   // no built website (development runs it on the Vite dev server)
        http.Response.Headers.CacheControl = "no-cache";
        html = meta is null ? InHead(html, NoIndex) : WithMeta(html, meta, Origin(http));
        if (content is not null) html = WithContent(html, content);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: meta is null ? StatusCodes.Status404NotFound : StatusCodes.Status200OK);
    }

    /// <summary>
    /// Puts the page's content inside the app's root element (the app replaces it when it starts) and its schema.org
    /// data in the head.
    /// </summary>
    public static string WithContent(string html, PageContent content)
    {
        var root = RootElement().Match(html);
        if (root.Success)
            html = html.Remove(root.Index, root.Length).Insert(root.Index, $"{root.Groups["open"].Value}<main class=\"ssr\">{content.Body}</main></div>");
        if (content.StructuredData is { } data)
        {
            var at = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) html = html.Insert(at, "    " + PageRenderer.JsonLd(data) + "\n");
        }
        return html;
    }

    [GeneratedRegex(@"(?<open><div\s+id=""root""[^>]*>)\s*</div>", RegexOptions.IgnoreCase)]
    private static partial Regex RootElement();

    /// <summary>
    /// Replaces the title and description and adds the canonical address, Open Graph tags and the breadcrumb trail (or
    /// keeps the page out of search results).
    /// </summary>
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
                <meta property="og:image" content="{enc.Encode(origin + ShareImage)}" />
                <meta property="og:image:width" content="1200" />
                <meta property="og:image:height" content="630" />
                <meta property="og:image:alt" content="CompanyPaisa: public companies near you. Revenue, profit, executive pay and salaries." />
                <meta name="twitter:card" content="summary_large_image" />

            """;
        if (!meta.Index) head += NoIndex;
        if (meta.Index && meta.Trail is { Count: > 0 } trail) head += "    " + PageRenderer.JsonLd(Breadcrumbs(origin, trail)) + "\n";
        return InHead(html, head);
    }

    /// <summary>The picture link previews show (1200×630, in the website's public folder).</summary>
    public const string ShareImage = "/og-image.png";

    private const string NoIndex ="    <meta name=\"robots\" content=\"noindex\" />\n";

    private static string InHead(string html, string tags)
    {
        var at = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? html : html.Insert(at, tags);
    }

    /// <summary>schema.org BreadcrumbList: the home page, then the trail.</summary>
    private static Dictionary<string, object?> Breadcrumbs(string origin, IEnumerable<(string Name, string Path)> trail) => new()
    {
        ["@context"] = "https://schema.org", ["@type"] = "BreadcrumbList",
        ["itemListElement"] = trail.Prepend((Name: "CompanyPaisa", Path: "/")).Select((step, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem", ["position"] = i + 1, ["name"] = step.Name, ["item"] = origin + step.Path
        }).ToList()
    };

    private static string CompanyPath(string ticker) => $"/company/{Uri.EscapeDataString(ticker.ToUpperInvariant())}";

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
