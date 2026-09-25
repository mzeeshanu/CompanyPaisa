using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Endpoints;

/// <summary>What a page shows before the app starts: readable HTML and schema.org data for search engines.</summary>
public sealed record PageContent(string Body, object? StructuredData = null);

/// <summary>
/// Writes each page's facts as plain HTML into the page the server sends: the company's figures, its executives' pay,
/// its pay ratio and job salaries, with links between pages. Search engines and link previews read it without running
/// the app, and visitors see it while the app loads (the app then draws the full page in its place). Reads the
/// repository directly, so rendering a page isn't counted as a visit.
/// </summary>
public sealed class PageRenderer(
    ICompanyRepository repository,
    INearbySearchService nearby,
    IFinancialMetricsService metrics,
    ICurrencyConverter currency,
    IOptionsMonitor<SearchOptions> search,
    IOptionsMonitor<UiOptions> ui,
    IOptionsMonitor<FeatureOptions> features)
{
    private static readonly HtmlEncoder Enc = HtmlEncoder.Default;
    private bool Executives => features.CurrentValue.IsEnabled("Executives");

    // ---- Company -------------------------------------------------------------------------------------------------

    public async Task<PageContent> CompanyAsync(Company c, CancellationToken ct)
    {
        var locations = await repository.GetLocationsAsync(c.CompanyId, ct);
        var hq = locations.FirstOrDefault(l => l.IsHeadquarters) ?? locations.FirstOrDefault();
        var indicators = metrics.Compute(await repository.GetFinancialsAsync(c.CompanyId, ct));
        var pay = Executives ? await repository.GetExecutiveCompensationAsync(c.CompanyId, ct) : [];
        var ratio = (await repository.GetWorkerPayAsync(c.CompanyId, ct)).MaxBy(w => w.Year);
        var jobs = (await repository.GetJobSalariesAsync(c.CompanyId, ct)).Where(j => j.City is null).OrderByDescending(j => j.Filings).ToList();
        var payCurrency = c.PayCurrency ?? c.Currency;

        var h = new Html();
        h.Open("h1").Text($"{c.Name} ({c.Ticker})").Close("h1");
        h.Open("p").Text(string.Join(" · ", new[] { c.Sector, c.Industry, c.Exchange }.Where(s => !string.IsNullOrWhiteSpace(s))));
        if (hq is not null) h.Text($" · Headquarters: {Address(hq)}");
        h.Close("p");
        if (c.Website is not null || c.CareersUrl is not null)
        {
            h.Open("p");
            if (c.Website is not null) h.Link(c.Website, "Website", external: true);
            if (c.Website is not null && c.CareersUrl is not null) h.Text(" · ");
            if (c.CareersUrl is not null) h.Link(c.CareersUrl, "Careers", external: true);
            h.Close("p");
        }

        if (indicators.AnnualHistory.Count > 0 || indicators.TtmRevenue != 0)
        {
            h.Open("h2").Text("Key figures").Close("h2").Open("ul");
            h.Item($"Revenue (last 12 months): {Money(indicators.TtmRevenue, c.Currency)}");
            h.Item($"Net income (last 12 months): {Money(indicators.TtmNetIncome, c.Currency)}");
            if (indicators.NetMargin is { } margin) h.Item($"Net margin: {Percent(margin)}");
            if (indicators.RevenueGrowthYoY is { } growth) h.Item($"Revenue growth: {Signed(growth)} on the year before");
            if (c.Employees is { } employees) h.Item($"Employees: {employees:N0}");
            h.Close("ul");
        }
        if (indicators.AnnualHistory.Count > 0)
        {
            h.Open("h2").Text("Revenue and profit by year").Close("h2");
            h.Table(["Year", "Revenue", "Net income"],
                indicators.AnnualHistory.OrderByDescending(p => p.FiscalYear).Select(p => new[] { $"FY {p.FiscalYear}", Money(p.Revenue, c.Currency), Money(p.NetIncome, c.Currency) }));
        }

        if (pay.Count > 0)
        {
            var year = pay.Max(p => p.Year);
            h.Open("h2").Text($"Executive pay ({year})").Close("h2");
            h.Open("table").Open("thead").Open("tr").Cell("th", "Executive").Cell("th", "Role").Cell("th", "Total pay").Close("tr").Close("thead").Open("tbody");
            foreach (var p in pay.Where(p => p.Year == year).OrderByDescending(p => p.Total))
            {
                h.Open("tr").Open("td").Link($"/executive/{Uri.EscapeDataString(p.PersonId)}", p.ExecutiveName).Close("td")
                    .Cell("td", p.Title).Cell("td", Money(p.Total, payCurrency)).Close("tr");
            }
            h.Close("tbody").Close("table");
        }

        if (ratio is not null)
        {
            h.Open("h2").Text("CEO pay and the typical employee").Close("h2");
            h.Open("p").Text($"In {ratio.Year} the CEO's pay was {Money(ratio.CeoPay, "USD")}, {ratio.Ratio:0.#} times the {Money(ratio.MedianEmployeePay, "USD")} " +
                             $"the company's median employee earned (as the company disclosed it in its proxy statement).").Close("p");
        }

        foreach (var source in (jobs.Count > 0 ? await repository.GetJobSalarySourcesAsync(ct) : []).OrderBy(s => s.Kind == JobSalary.JobAds ? 0 : 1))
        {
            var mine = jobs.Where(j => j.Source == source.Kind).ToList();
            if (mine.Count == 0) continue;
            var ads = source.Kind == JobSalary.JobAds;
            h.Open("h2").Text(ads ? "Salaries in its job ads" : "Salaries in its work-visa filings").Close("h2");
            h.Open("p").Text(ads
                ? $"The pay ranges {c.Name} advertised in its US job ads, {source.From:MMM yyyy} to {source.To:MMM yyyy}: the typical range for each job title."
                : $"What {c.Name} offered in its US work-visa (H-1B) wage filings, {source.From:MMM yyyy} to {source.To:MMM yyyy}. Median yearly salary, and the middle half of the offers.").Close("p");
            h.Table(["Job title", ads ? "Ads" : "Filings", ads ? "Middle" : "Median", ads ? "Typical range" : "Middle half"],
                mine.Take(25).Select(j => new[] { j.Title, j.Filings.ToString("N0", CultureInfo.InvariantCulture), Money(j.Median, "USD"), $"{Money(j.Low, "USD")} – {Money(j.High, "USD")}" }));
            if (mine.Count > 25) h.Open("p").Text($"And {mine.Count - 25:N0} more job titles.").Close("p");
        }

        if (locations.Count > 1)
        {
            h.Open("h2").Text("Locations").Close("h2").Open("ul");
            foreach (var l in locations.OrderBy(l => l.IsHeadquarters ? 0 : 1).ThenBy(l => l.City)) h.Item($"{l.Label}: {Address(l)}");
            h.Close("ul");
        }
        h.Open("p").Text("Figures are from the company's own filings. ").Link("/", "Find public companies near you").Close("p");

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Corporation", ["name"] = c.Name, ["tickerSymbol"] = $"{c.Exchange} {c.Ticker}",
            ["url"] = c.Website,
            ["numberOfEmployees"] = c.Employees is { } n ? new Dictionary<string, object> { ["@type"] = "QuantitativeValue", ["value"] = n } : null,
            ["address"] = hq is null ? null : new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress", ["streetAddress"] = NullIfEmpty(hq.Street), ["addressLocality"] = hq.City, ["addressRegion"] = hq.State,
                ["postalCode"] = NullIfEmpty(hq.PostalCode)
            }
        };
        return new PageContent(h.ToString(), data);
    }

    // ---- Salaries ------------------------------------------------------------------------------------------------

    /// <summary>A company's salaries on their own page: every job title from each source, with its places.</summary>
    public async Task<PageContent> SalariesAsync(Company c, CancellationToken ct)
    {
        var rows = await repository.GetJobSalariesAsync(c.CompanyId, ct);
        var sources = await repository.GetJobSalarySourcesAsync(ct);
        var h = new Html();
        h.Open("h1").Text($"Salaries at {c.Name} ({c.Ticker})").Close("h1");
        h.Open("p").Text("What the company pays by job title, from its own US job ads and its work-visa wage filings. ")
            .Link(CompanyPath(c), $"{c.Name}'s revenue, profit and executive pay").Close("p");

        foreach (var source in sources.OrderBy(s => s.Kind == JobSalary.JobAds ? 0 : 1))
        {
            var titles = rows.Where(r => r.Source == source.Kind && r.City is null).OrderByDescending(r => r.Filings).ToList();
            if (titles.Count == 0) continue;
            var ads = source.Kind == JobSalary.JobAds;
            var places = rows.Where(r => r.Source == source.Kind && r.City is not null).ToLookup(r => r.Title, StringComparer.Ordinal);
            h.Open("h2").Text(ads ? $"In {c.Name}'s job ads ({source.From:MMM yyyy} – {source.To:MMM yyyy})" : $"In its work-visa filings ({source.From:MMM yyyy} – {source.To:MMM yyyy})").Close("h2");
            h.Open("p").Text(ads
                ? "US pay-transparency laws require a pay range in the ad. Below: how many ads, the typical middle of the range, and the typical bottom to top."
                : "The salary the company committed to pay. Below: how many filings, the median, and the middle half of the offers.").Close("p");
            h.Table(["Job title", ads ? "Ads" : "Filings", ads ? "Middle" : "Median", ads ? "Typical range" : "Middle half", "Where"],
                titles.Select(t => new[]
                {
                    t.Title, t.Filings.ToString("N0", CultureInfo.InvariantCulture), Money(t.Median, "USD"), $"{Money(t.Low, "USD")} – {Money(t.High, "USD")}",
                    string.Join(", ", places[t.Title].OrderByDescending(p => p.Filings).Take(4).Select(p => $"{p.City}, {p.State} {Money(p.Median, "USD")}"))
                }));
        }
        h.Open("p").Text("Figures are from the company's own job ads and filings. ").Link("/", "Find public companies near you").Close("p");
        return new PageContent(h.ToString());
    }

    // ---- Executive -----------------------------------------------------------------------------------------------

    public async Task<PageContent> ExecutiveAsync(Person person, CancellationToken ct)
    {
        var rows = (await repository.GetCompensationForPeopleAsync([person.PersonId], ct)).OrderByDescending(r => r.Year).ToList();
        var companies = (await repository.GetCompaniesAsync(rows.Select(r => r.CompanyId).Distinct(), ct)).ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
        var latest = rows.FirstOrDefault();
        var company = latest is null ? null : companies.GetValueOrDefault(latest.CompanyId);

        var h = new Html();
        h.Open("h1").Text(person.Name).Close("h1");
        if (latest is not null && company is not null)
            h.Open("p").Text($"{latest.Title} at ").Link(CompanyPath(company), company.Name).Text($" (latest filing: {latest.Year})").Close("p");
        if (rows.Count > 0)
        {
            h.Open("h2").Text("Pay by year").Close("h2");
            h.Open("table").Open("thead").Open("tr").Cell("th", "Year").Cell("th", "Company").Cell("th", "Role").Cell("th", "Salary").Cell("th", "Total pay").Close("tr").Close("thead").Open("tbody");
            foreach (var r in rows)
            {
                var c = companies.GetValueOrDefault(r.CompanyId);
                var cur = c?.PayCurrency ?? c?.Currency ?? "USD";
                h.Open("tr").Cell("td", r.Year.ToString(CultureInfo.InvariantCulture)).Open("td");
                if (c is not null) h.Link(CompanyPath(c), c.Name); else h.Text(r.CompanyId);
                h.Close("td").Cell("td", r.Title).Cell("td", Money(r.Salary, cur)).Cell("td", Money(r.Total, cur)).Close("tr");
            }
            h.Close("tbody").Close("table");
        }
        h.Open("p").Text("Pay as reported in the companies' summary compensation tables. ").Link("/", "Find public companies near you").Close("p");

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Person", ["name"] = person.Name, ["jobTitle"] = latest?.Title,
            ["worksFor"] = company is null ? null : new Dictionary<string, object?> { ["@type"] = "Corporation", ["name"] = company.Name }
        };
        return new PageContent(h.ToString(), data);
    }

    // ---- Search (near a place) -----------------------------------------------------------------------------------

    /// <summary>A search page: near a point (the default radius), or every company in a whole country or state.</summary>
    public async Task<PageContent> NearAsync(GeoPoint origin, string where, bool executives, CancellationToken ct, Region? region = null)
    {
        var radius = search.CurrentValue.DefaultRadiusMiles;
        var hits = region is null ? await nearby.FindCompaniesAsync(origin, radius, ct) : await nearby.FindCompaniesInRegionAsync(region, ct);
        var nearWhere = region is null ? $"near {where}" : $"in {where}";
        var within = region is null ? $"within {radius:0} miles" : $"in {where}";
        var companies = await repository.GetCompaniesAsync(hits.Keys, ct);
        var financials = await repository.GetFinancialsAsync(hits.Keys, ct);
        var ranked = companies
            .Select(c => (Company: c, Indicators: metrics.Compute(financials.GetValueOrDefault(c.CompanyId) ?? []), Hit: hits[c.CompanyId]))
            .OrderByDescending(x => currency.ToUsd(x.Indicators.TtmRevenue, x.Company.Currency)).ToList();

        var h = new Html();
        if (executives && Executives)
        {
            h.Open("h1").Text($"Executives of public companies {nearWhere}").Close("h1");
            var pay = await repository.GetExecutiveCompensationAsync(hits.Keys, ct);
            var byCompany = companies.ToDictionary(c => c.CompanyId, StringComparer.OrdinalIgnoreCase);
            var latest = pay.GroupBy(p => p.PersonId).Select(g => g.MaxBy(p => p.Year)!)
                .OrderByDescending(p => currency.ToUsd(p.Total, byCompany[p.CompanyId].PayCurrency ?? byCompany[p.CompanyId].Currency)).Take(100).ToList();
            h.Open("p").Text($"The best-paid named executives of the {companies.Count:N0} public companies {within}, by their latest reported pay.").Close("p");
            h.Open("table").Open("thead").Open("tr").Cell("th", "Executive").Cell("th", "Company").Cell("th", "Role").Cell("th", "Total pay").Close("tr").Close("thead").Open("tbody");
            foreach (var p in latest)
            {
                var c = byCompany[p.CompanyId];
                h.Open("tr").Open("td").Link($"/executive/{Uri.EscapeDataString(p.PersonId)}", p.ExecutiveName).Close("td")
                    .Open("td").Link(CompanyPath(c), c.Name).Close("td").Cell("td", p.Title)
                    .Cell("td", $"{Money(p.Total, c.PayCurrency ?? c.Currency)} ({p.Year})").Close("tr");
            }
            h.Close("tbody").Close("table");
        }
        else
        {
            h.Open("h1").Text($"Public companies {nearWhere}").Close("h1");
            h.Open("p").Text($"{companies.Count:N0} public companies have an office {within}. The largest by revenue:").Close("p");
            h.Open("table").Open("thead").Open("tr").Cell("th", "Company").Cell("th", "Where").Cell("th", "Sector").Cell("th", "Revenue (12 months)").Close("tr").Close("thead").Open("tbody");
            foreach (var x in ranked.Take(100))
            {
                h.Open("tr").Open("td").Link(CompanyPath(x.Company), $"{x.Company.Name} ({x.Company.Ticker})").Close("td")
                    .Cell("td", region is null ? $"{x.Hit.NearestLocation.City}, {x.Hit.NearestLocation.State} ({x.Hit.DistanceMiles:0.#} mi)"
                        : $"{x.Hit.NearestLocation.City}, {x.Hit.NearestLocation.State}")
                    .Cell("td", x.Company.Sector).Cell("td", x.Indicators.TtmRevenue == 0 ? "—" : Money(x.Indicators.TtmRevenue, x.Company.Currency)).Close("tr");
            }
            h.Close("tbody").Close("table");
        }
        h.Open("p").Link("/", "Search another place").Close("p");
        return new PageContent(h.ToString());
    }

    // ---- Home ----------------------------------------------------------------------------------------------------

    public async Task<PageContent> HomeAsync(CancellationToken ct)
    {
        var companies = await repository.GetCompaniesAsync(ct);
        var financials = await repository.GetFinancialsAsync(companies.Select(c => c.CompanyId), ct);
        var largest = companies
            .Select(c => (Company: c, Revenue: metrics.Compute(financials.GetValueOrDefault(c.CompanyId) ?? []).TtmRevenue))
            .OrderByDescending(x => currency.ToUsd(x.Revenue, x.Company.Currency)).Take(50).ToList();

        var h = new Html();
        h.Open("h1").Text("Public companies near you: revenue, profit and executive pay").Close("h1");
        h.Open("p").Text($"CompanyPaisa maps {companies.Count:N0} public companies in the US, Canada, the UK, Europe, Australia, New Zealand and Pakistan: how big they are, " +
                         "whether they're growing, what their executives are paid and what their jobs pay — all from the companies' own filings.").Close("p");
        h.Open("h2").Text("Browse by area").Close("h2").Open("ul");
        foreach (var area in ui.CurrentValue.Coverage.DistinctBy(a => a.Name))
            h.Open("li").Link($"/near/{Uri.EscapeDataString(SitePages.PlaceToken(area.ExampleZip, area.Country))}", area.Name).Close("li");
        h.Close("ul");
        h.Open("h2").Text("The largest companies").Close("h2").Open("ol");
        foreach (var (c, revenue) in largest)
            h.Open("li").Link(CompanyPath(c), $"{c.Name} ({c.Ticker})").Text($" — {Money(revenue, c.Currency)} revenue").Close("li");
        h.Close("ol");

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "WebSite", ["name"] = "CompanyPaisa",
            ["description"] = "Public companies near you: revenue, profit, executive pay and salaries by job title."
        };
        return new PageContent(h.ToString(), data);
    }

    // ---- Formatting ----------------------------------------------------------------------------------------------

    private static string CompanyPath(Company c) => $"/company/{Uri.EscapeDataString(c.Ticker.ToUpperInvariant())}";

    private static string Address(CompanyLocation l) =>
        string.Join(", ", new[] { l.Street, l.City, $"{l.State} {l.PostalCode}".Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Percent(decimal fraction) => (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Signed(decimal fraction) => (fraction >= 0 ? "+" : "") + Percent(fraction);

    /// <summary>"$394.3B", "£1.2M", "Rs 45.6B", "€820K": the way the app shows amounts.</summary>
    internal static string Money(decimal amount, string currencyCode)
    {
        var symbol = currencyCode.ToUpperInvariant() switch
        {
            "USD" => "$", "GBP" => "£", "EUR" => "€", "PKR" => "Rs ", "CAD" => "C$", "AUD" => "A$", "NZD" => "NZ$", _ => currencyCode + " "
        };
        var abs = Math.Abs(amount);
        var (value, suffix) = abs switch
        {
            >= 1_000_000_000_000m => (abs / 1_000_000_000_000m, "T"),
            >= 1_000_000_000m => (abs / 1_000_000_000m, "B"),
            >= 1_000_000m => (abs / 1_000_000m, "M"),
            >= 10_000m => (abs / 1_000m, "K"),
            _ => (abs, "")
        };
        var digits = suffix.Length == 0 ? value.ToString("N0", CultureInfo.InvariantCulture) : value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return $"{(amount < 0 ? "-" : "")}{symbol}{digits}{suffix}";
    }

    /// <summary>A small HTML writer that encodes every piece of text.</summary>
    private sealed class Html
    {
        private readonly StringBuilder _sb = new();
        public Html Open(string tag) { _sb.Append('<').Append(tag).Append('>'); return this; }
        public Html Close(string tag) { _sb.Append("</").Append(tag).Append('>'); return this; }
        public Html Text(string text) { _sb.Append(Enc.Encode(text)); return this; }
        public Html Item(string text) => Open("li").Text(text).Close("li");
        public Html Cell(string tag, string text) => Open(tag).Text(text).Close(tag);

        public Html Link(string href, string text, bool external = false)
        {
            _sb.Append("<a href=\"").Append(Enc.Encode(href)).Append('"');
            if (external) _sb.Append(" rel=\"nofollow noopener\"");
            _sb.Append('>').Append(Enc.Encode(text)).Append("</a>");
            return this;
        }

        public Html Table(string[] headers, IEnumerable<string[]> rows)
        {
            Open("table").Open("thead").Open("tr");
            foreach (var header in headers) Cell("th", header);
            Close("tr").Close("thead").Open("tbody");
            foreach (var row in rows)
            {
                Open("tr");
                foreach (var cell in row) Cell("td", cell);
                Close("tr");
            }
            return Close("tbody").Close("table");
        }

        public override string ToString() => _sb.ToString();
    }

    /// <summary>schema.org data as a script tag, with "&lt;" escaped so no value can end the script early.</summary>
    public static string JsonLd(object data) =>
        "<script type=\"application/ld+json\">" + JsonSerializer.Serialize(Prune(data)).Replace("<", "\\u003c") + "</script>";

    /// <summary>Leaves out the fields a page has no value for.</summary>
    private static object? Prune(object? value) => value is Dictionary<string, object?> d
        ? d.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => Prune(kv.Value))
        : value;
}
