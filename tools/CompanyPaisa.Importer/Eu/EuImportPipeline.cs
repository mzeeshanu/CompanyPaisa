using System.Globalization;
using System.Text;
using CompanyPaisa.Contracts;
using CompanyPaisa.Core.Domain;
using CompanyPaisa.Core.Services;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Importer.Sec;
using CompanyPaisa.Importer.Uk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Importer.Eu;

/// <summary>
/// Builds the European workbook — financials only, no executives:
/// ESEF filers per country (filings.xbrl.org) → shares listed on the home exchange (GLEIF ISINs → OpenFIGI) →
/// headquarters (GLEIF) → postcode (GeoNames) → ~6 years of IFRS revenue and profit from the reports' xBRL-JSON.
/// </summary>
public sealed class EuImportPipeline(IOptions<ImporterOptions> options, RepoPaths paths, ILoggerFactory loggers)
{
    private readonly EuOptions _o = options.Value.Eu;
    private readonly ILogger _log = loggers.CreateLogger<EuImportPipeline>();
    private readonly HaversineDistanceCalculator _distance = new();

    public async Task<int> RunAsync(CancellationToken ct)
    {
        using var client = new SecClient(new SecOptions
        {
            UserAgent = _o.UserAgent, MaxRequestsPerSecond = _o.MaxRequestsPerSecond, CacheDirectory = _o.CacheDirectory,
            IndexCacheHours = _o.IndexCacheHours, CompressCache = true
        }, paths, loggers.CreateLogger<SecClient>());

        var postcodes = new EuPostcodes();
        var companies = new List<Company>();
        var locations = new List<CompanyLocation>();
        var periods = new List<FinancialPeriod>();
        var included = new List<(string Country, string Region, string Line)>();
        var excluded = new List<string>();
        var warnings = new List<string>();
        var found = new List<(string Country, UkConstituent Company)>();

        foreach (var country in _o.Countries)
        {
            await postcodes.LoadAsync(client, _o.PostcodesUrl, country.Code, ct);
            var entities = await UkFilingsIndex.LoadAsync(client, _o.FilingsApi, ct, country.Code);
            _log.LogInformation("{Country}: {Entities} ESEF filers", country.Name, entities.Count);

            using var market = new UkMainMarket(client, _o.GleifApi, paths.Resolve(_o.CacheDirectory), _log,
                country.ExchangeCodes, [country.Code], $"openfigi-{country.Code.ToLowerInvariant()}.json");
            var listed = await market.FindAsync(entities, new HashSet<string>(StringComparer.OrdinalIgnoreCase), _o.RecentYears, ct);
            var byLei = entities.ToDictionary(e => e.Lei, StringComparer.OrdinalIgnoreCase);

            foreach (var c in listed.OrderBy(c => c.Name))
            {
                ct.ThrowIfCancellationRequested();
                found.Add((country.Code, c));
                var entity = byLei[c.Lei!];
                var companyId = c.Ticker + country.TickerSuffix;

                // Headquarters in the country itself (a Dutch-registered holding run from Paris counts as French only if it files there).
                var (hq, legal) = await Gleif.GetAddressesAsync(client, _o.GleifApi, entity.Lei, ct);
                var address = hq?.Country == country.Code ? hq : hq is null && legal?.Country == country.Code ? legal : null;
                if (address is null) { excluded.Add($"{companyId} {c.Name}: headquartered outside {country.Name} ({hq?.City}, {hq?.Country})"); continue; }
                if (postcodes.Locate(country.Code, address.PostalCode) is not { } place) { excluded.Add($"{companyId} {c.Name}: unknown postcode {address.PostalCode}"); continue; }

                var years = await EsefFinancials.CollectYearsAsync(client, entity, companyId, warnings, ct);
                if (years.Count == 0) { excluded.Add($"{companyId} {c.Name}: no revenue in its tagged reports"); continue; }
                var latest = years.Values.MaxBy(y => y.Year.FiscalYear).Year;
                var region = RegionFor(country, place.Point);

                companies.Add(new Company
                {
                    CompanyId = companyId, Ticker = companyId, Name = c.Name, Exchange = country.Exchange, Sector = "Other", Industry = "",
                    Currency = latest.Currency, FiscalYearEnd = latest.PeriodEnd.ToString("MM-dd", CultureInfo.InvariantCulture), AsOfDate = latest.PeriodEnd
                });
                locations.Add(new CompanyLocation
                {
                    LocationId = $"{companyId}-HQ", CompanyId = companyId, Type = LocationType.Headquarters, Label = "Headquarters",
                    Street = address.Street, City = string.IsNullOrWhiteSpace(address.City) ? place.Place : TitleCase(address.City),
                    State = country.Code, PostalCode = address.PostalCode.ToUpperInvariant(), Point = place.Point
                });
                periods.AddRange(years.Values.Select(y => EsefFinancials.ToPeriod(companyId, y.Year, y.Source)));
                included.Add((country.Name, region, string.Create(CultureInfo.InvariantCulture,
                    $"| {companyId} | {c.Name} | {TitleCase(address.City)} | {Money(latest.Revenue, latest.Currency)} | {years.Keys.Min()}–{years.Keys.Max()} |")));
                _log.LogInformation("{Ticker,-9} {Name}: {Years} years, {Region}", companyId, c.Name, years.Count, region);
            }
        }

        UkConstituents.Write(paths.Resolve(_o.ListPath), found.OrderBy(f => f.Country).ThenBy(f => f.Company.Name)
            .Select(f => f.Company with { Sector = f.Country }));

        var workbook = paths.Resolve(_o.WorkbookPath);
        var staging = workbook + ".new.xlsx";
        ExcelWorkbookWriter.Write(staging, companies, locations, periods, [], [], new Dictionary<string, string>
        {
            ["data_version"] = $"eu-{DateTime.UtcNow:yyyy.MM.dd}",
            ["as_of_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["is_sample"] = "false",
            ["source"] = "ESEF annual reports via filings.xbrl.org; tickers via GLEIF ISINs and OpenFIGI; headquarters from GLEIF; postcodes from GeoNames.",
            ["region"] = string.Join(", ", _o.Countries.Select(c => c.Name))
        });
        var (vc, _) = ExcelWorkbookWriter.Verify(staging);
        File.Move(staging, workbook, overwrite: true);
        var codes = await postcodes.WriteTableAsync(paths.Resolve(_o.PostcodeTableOutput), ct);
        await WriteReportAsync(companies.Count, periods.Count, codes, included, excluded, warnings, client.NetworkRequests, ct);
        _log.LogInformation("Done: {Companies} European companies ({Verified} verified in the API loader), {Periods} years of figures → {Path}",
            companies.Count, vc, periods.Count, workbook);
        return 0;
    }

    private string RegionFor(EuCountryOptions country, GeoPoint point)
    {
        foreach (var r in country.Regions)
            if (r.Anchors.Any(a => _distance.DistanceMiles(point, new GeoPoint(a.Latitude, a.Longitude)) <= a.RadiusMiles)) return r.Name;
        return country.Regions.FirstOrDefault(r => r.WholeCountry)?.Name ?? country.Name;
    }

    private static string TitleCase(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Any(char.IsLower) ? s.Trim() : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Trim().ToLowerInvariant());

    private static string Money(decimal v, string currency)
    {
        var sym = currency switch { "EUR" => "€", "GBP" => "£", "USD" => "$", _ => currency + " " };
        return v >= 1_000_000_000 ? $"{sym}{v / 1_000_000_000:0.00}B" : v >= 1_000_000 ? $"{sym}{v / 1_000_000:0.0}M" : $"{sym}{v / 1_000:0}K";
    }

    private async Task WriteReportAsync(int companies, int periods, int codes, List<(string Country, string Region, string Line)> included,
        List<string> excluded, List<string> warnings, int requests, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# European import report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").AppendLine();
        sb.AppendLine($"Scope: {string.Join(", ", _o.Countries.Select(c => c.Name))} — companies with shares on the home exchange that file ESEF annual reports. " +
                      "Financials only (no executives). Sources: filings.xbrl.org, GLEIF, OpenFIGI, GeoNames.").AppendLine();
        sb.AppendLine($"- Companies included: **{companies}**");
        sb.AppendLine($"- Years of figures: {periods}");
        sb.AppendLine($"- Postcodes written: {codes}");
        sb.AppendLine($"- Network requests this run: {requests}").AppendLine();
        sb.AppendLine("| Country | Area | Companies |").AppendLine("|---|---|---|");
        foreach (var g in included.GroupBy(i => (i.Country, i.Region)).OrderBy(g => g.Key.Country).ThenByDescending(g => g.Count()))
            sb.AppendLine($"| {g.Key.Country} | {g.Key.Region} | {g.Count()} |");
        foreach (var g in included.GroupBy(i => i.Country))
        {
            sb.AppendLine().AppendLine($"## Included — {g.Key} ({g.Count()})").AppendLine();
            sb.AppendLine("| Ticker | Company | City | Latest revenue | Years |").AppendLine("|---|---|---|---|---|");
            foreach (var line in g.Select(i => i.Line).Order()) sb.AppendLine(line);
        }
        sb.AppendLine().AppendLine($"## Excluded ({excluded.Count})").AppendLine();
        foreach (var line in excluded.Order()) sb.AppendLine($"- {line}");
        sb.AppendLine().AppendLine("## Needs review").AppendLine();
        foreach (var line in warnings) sb.AppendLine($"- {line}");
        await File.WriteAllTextAsync(paths.Resolve(_o.ReportPath), sb.ToString(), ct);
    }
}
