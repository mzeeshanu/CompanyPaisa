// Builds the website's database (data/companypaisa.db) from companies' official filings — offline, never inside the website.
// Usage (from the repo root):  dotnet run --project tools/CompanyPaisa.Importer -- --all   (see "Markets" below)
// Settings: tools/CompanyPaisa.Importer/appsettings.json ("Importer" section). Downloads are cached in data/cache.

using CompanyPaisa.Importer;
using CompanyPaisa.Importer.Compensation;
using CompanyPaisa.Importer.Financials;
using CompanyPaisa.Importer.Geo;
using CompanyPaisa.Importer.Publishing;
using CompanyPaisa.Importer.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
// Personal settings (the SEC contact email) live in appsettings.Local.json, which git ignores.
// Environment variable alternative: Importer__Sec__UserAgent="CompanyPaisa you@example.com"
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddOptions<ImporterOptions>().Bind(builder.Configuration.GetSection(ImporterOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<RepoPaths>();
builder.Services.AddSingleton<ISecClient, SecClient>();
builder.Services.AddSingleton<IEdgarService, EdgarService>();
builder.Services.AddSingleton<IFinancialsExtractor, XbrlFinancialsExtractor>();
builder.Services.AddSingleton<ICompensationParser, SummaryCompensationTableParser>();
builder.Services.AddSingleton<IZipGeocoder, ZipGeocoder>();
builder.Services.AddSingleton<NewHireReader>();
builder.Services.AddSingleton<ImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Uk.UkImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Eu.EuImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Publishing.DataPublisher>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Publishing.LegacyWorkbookMigration>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Validation.ValidationRun>();
// The markets, in the order --all runs them.
builder.Services.AddSingleton<IMarketImporter>(sp => sp.GetRequiredService<ImportPipeline>());
builder.Services.AddSingleton<IMarketImporter>(sp => sp.GetRequiredService<CompanyPaisa.Importer.Uk.UkImportPipeline>());
builder.Services.AddSingleton<IMarketImporter>(sp => sp.GetRequiredService<CompanyPaisa.Importer.Eu.EuImportPipeline>());
builder.Services.AddSingleton<MarketRunner>();

using var host = builder.Build();

// Diagnostics for one annual report: -- --debug-uk-pay <report xhtml url>
if (args is ["--debug-uk-pay", var reportUrl])
{
    var html = await new HttpClient { Timeout = TimeSpan.FromMinutes(5), DefaultRequestHeaders = { { "User-Agent", "CompanyPaisa" } } }.GetStringAsync(reportUrl);
    foreach (var line in CompanyPaisa.Importer.Uk.RemunerationParser.Describe(html).Take(400)) Console.WriteLine(line);
    var parsed = CompanyPaisa.Importer.Uk.RemunerationParser.Parse(html);
    foreach (var r in parsed.Rows)
        Console.WriteLine($"{r.Name} | {r.Year} | salary {r.Salary:N0} bonus {r.Bonus:N0} long-term {r.LongTerm:N0} other {r.Other:N0} total {r.Total:N0} {parsed.Currency} {(r.Verified ? "✓" : "✗")}");
    foreach (var w in parsed.Warnings) Console.WriteLine("warning: " + w);
    return 0;
}
// Diagnostics for new-hire packages (8-K Item 5.02): -- --debug-new-hires <filing url>
if (args is ["--debug-new-hires", var hireUrl])
{
    var html = await host.Services.GetRequiredService<ISecClient>().GetStringAsync(hireUrl, CachePolicy.Immutable) ?? "";
    Console.WriteLine(NewHireParser.Item502(NewHireParser.PlainText(html)) ?? "(no Item 5.02)");
    Console.WriteLine();
    foreach (var h in NewHireParser.Parse(html))
        Console.WriteLine($"{h.Name} | {h.Title} | starts {h.StartsOn} | total {h.Total:N0} | " + string.Join("; ", h.Parts.Select(p => $"{p.Label} {p.Amount:N0}")));
    return 0;
}
// Try the new-hire reader on the officer-change 8-Ks of N companies from the database: -- --scan-new-hires <N> [months]
if (args is ["--scan-new-hires", var count, .. var rest])
{
    var edgar = host.Services.GetRequiredService<IEdgarService>();
    var paths = host.Services.GetRequiredService<RepoPaths>();
    var months = rest.Length > 0 ? int.Parse(rest[0]) : 18;
    var since = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-months));
    var data = CompanyPaisa.Data.Sqlite.SqliteDataStore.Read(paths.Resolve("data/companypaisa.db"), DateTimeOffset.UtcNow);
    // A number = an evenly spread sample of that many US companies (the same each run); otherwise a list of tickers.
    var all = data.Companies.Where(c => !c.Ticker.Contains('.')).Select(c => c.Ticker).Order(StringComparer.Ordinal).ToList();
    var tickers = int.TryParse(count, out var n)
        ? all.Where((_, i) => i % Math.Max(1, all.Count / n) == 0).Take(n).ToList()
        : count.Split(',').ToList();
    int filings = 0, withHires = 0;
    foreach (var ticker in tickers)
    {
        if (await edgar.FindCikByTickerAsync(ticker, CancellationToken.None) is not { } cik) continue;
        var sec = await edgar.GetCompanyAsync(cik, since, CancellationToken.None);
        foreach (var f in sec?.Filings.Where(f => f.Form == "8-K" && f.Items.Contains("5.02") && f.FilingDate >= since) ?? [])
        {
            filings++;
            var html = await edgar.GetDocumentAsync(f.Url(cik), CancellationToken.None) ?? "";
            var hires = NewHireParser.Parse(html, f.FilingDate);
            if (hires.Count > 0) withHires++;
            foreach (var h in hires)
                Console.WriteLine($"{ticker} {f.FilingDate} | {h.Name} | {h.Title} | starts {h.StartsOn} | total {h.Total:N0} | " + string.Join("; ", h.Parts.Select(p => $"{p.Label} {p.Amount:N0}")) + $" | {f.Url(cik)}");
        }
    }
    Console.WriteLine($"{tickers.Count} companies, {filings} officer-change 8-Ks, {withHires} with a package read.");
    return 0;
}
// Officer appointments only, for the SEC companies already in the database (quicker than a full import): -- --new-hires
if (args.Contains("--new-hires"))
{
    var edgar = host.Services.GetRequiredService<IEdgarService>();
    var reader = host.Services.GetRequiredService<NewHireReader>();
    var path = host.Services.GetRequiredService<RepoPaths>().Resolve(host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImporterOptions>>().Value.Output.DatabasePath);
    var data = CompanyPaisa.Data.Sqlite.SqliteDataStore.Read(path, DateTimeOffset.UtcNow);
    var since = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImporterOptions>>().Value.NewHires.Months);
    var peopleByCompany = data.Pay.GroupBy(p => p.CompanyId, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.GroupBy(p => ImportPipeline.PersonId(p.ExecutiveName)).ToDictionary(x => x.Key, x => x.First().PersonId), StringComparer.OrdinalIgnoreCase);
    var found = new List<CompanyPaisa.Core.Domain.NewExecutive>();
    var secCompanies = data.Companies.Where(c => !c.Ticker.Contains('.')).ToList();
    var done = 0;
    foreach (var company in secCompanies)
    {
        if (++done % 250 == 0) Console.WriteLine($"{done}/{secCompanies.Count} companies, {found.Count} appointments so far");
        var cik = company.CompanyId.Length == 10 && long.TryParse(company.CompanyId, out var id) ? id : await edgar.FindCikByTickerAsync(company.Ticker, CancellationToken.None);
        if (cik is null) continue;
        var sec = await edgar.GetCompanyAsync(cik.Value, since, CancellationToken.None);
        if (sec is null) continue;
        found.AddRange(await reader.ReadAsync(sec, company.CompanyId, peopleByCompany.GetValueOrDefault(company.CompanyId) ?? new Dictionary<string, string>(), CancellationToken.None));
    }
    var linked = NewHireReader.LinkByUniqueName(found, data.People.Select(p => p.PersonId));
    CompanyPaisa.Data.Sqlite.SqliteDataStore.ReplaceNewExecutives(path, ImportPipeline.Market, linked);
    Console.WriteLine($"{linked.Count} appointments at {linked.Select(x => x.CompanyId).Distinct().Count()} companies ({linked.Count(x => x.PersonId is not null)} linked to a person) → {path}");
    return 0;
}
// One-off: copy the pre-SQLite workbooks into the database: -- --migrate-xlsx
if (args.Contains("--migrate-xlsx"))
    return host.Services.GetRequiredService<CompanyPaisa.Importer.Publishing.LegacyWorkbookMigration>().Run();
// Data-quality checks over the published database: -- --validate [--strict]  (report: data/validation-report.md)
if (args.Contains("--validate"))
    return await host.Services.GetRequiredService<CompanyPaisa.Importer.Validation.ValidationRun>().RunAsync(args.Contains("--strict"), CancellationToken.None);
// Diagnostics: dotnet run --project tools/CompanyPaisa.Importer -- --debug-proxy <filing url>
if (args is ["--debug-proxy", var url])
{
    var html = await host.Services.GetRequiredService<ISecClient>().GetStringAsync(url, CachePolicy.Immutable) ?? "";
    foreach (var line in SummaryCompensationTableParser.Describe(html)) Console.WriteLine(line);
    foreach (var row in new SummaryCompensationTableParser().Parse(html).Rows)
        Console.WriteLine($"{row.Name} | {row.Title} | {row.Year} | sal {row.Salary:N0} bonus {row.Bonus:N0} stock {row.StockAwards:N0} other {row.Other:N0} total {row.Total:N0} {(row.ComponentsVerified ? "✓" : "✗")}");
    return 0;
}
// Markets (each publishes its own rows into data/companypaisa.db):
//   -- --list-markets            what's available
//   -- --market sec [--market uk] one or more by id      (--uk and --eu still work; no arguments = --market sec)
//   -- --all [--strict]           every market, then the data-quality checks (what the monthly job runs)
//   add --refresh-lists to re-read curated company lists (e.g. the FTSE constituents)
var runner = host.Services.GetRequiredService<MarketRunner>();
if (args.Contains("--list-markets"))
{
    foreach (var m in runner.Markets) Console.WriteLine($"{m.Market,-6} {m.Description}");
    return 0;
}
var marketIds = args.Select((a, i) => a == "--market" && i + 1 < args.Length ? args[i + 1] : null).OfType<string>()
    .Concat(args.Contains("--uk") ? ["uk"] : []).Concat(args.Contains("--eu") ? ["eu"] : []).ToList();
var runAll = args.Contains("--all");
if (!runAll && marketIds.Count == 0 && args.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a is not ("--refresh-lists" or "--refresh-uk-list" or "--strict")))
{
    Console.Error.WriteLine($"Unknown option. Try --list-markets, --market <id>, --all, --validate, --migrate-xlsx. ({string.Join(' ', args)})");
    return 2;
}
if (!runAll && marketIds.Count == 0) marketIds.Add(ImportPipeline.Market);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
return await runner.RunAsync(runAll ? [] : marketIds, args.Contains("--refresh-lists") || args.Contains("--refresh-uk-list"), args.Contains("--strict"), cts.Token);
