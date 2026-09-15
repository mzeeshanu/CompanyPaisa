// Builds the real CompanyPaisa dataset from SEC EDGAR (offline — never runs inside the website).
// Usage (from the repo root):  dotnet run --project tools/CompanyPaisa.Importer
// Settings: tools/CompanyPaisa.Importer/appsettings.json ("Importer" section). Downloads are cached in data/cache/sec.

using CompanyPaisa.Importer;
using CompanyPaisa.Importer.Compensation;
using CompanyPaisa.Importer.Financials;
using CompanyPaisa.Importer.Geo;
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
builder.Services.AddSingleton<ImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Uk.UkImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Eu.EuImportPipeline>();
builder.Services.AddSingleton<CompanyPaisa.Importer.Validation.ValidationRun>();

using var host = builder.Build();

// UK market (FTSE 350): dotnet run --project tools/CompanyPaisa.Importer -- --uk [--refresh-uk-list]
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
// Data-quality checks over every published workbook: -- --validate [--strict]  (report: data/validation-report.md)
if (args.Contains("--validate"))
    return await host.Services.GetRequiredService<CompanyPaisa.Importer.Validation.ValidationRun>().RunAsync(args.Contains("--strict"), CancellationToken.None);
// Europe (France, Netherlands, Italy, Spain — financials only): dotnet run --project tools/CompanyPaisa.Importer -- --eu
if (args.Contains("--eu"))
{
    using var euCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; euCts.Cancel(); };
    return await host.Services.GetRequiredService<CompanyPaisa.Importer.Eu.EuImportPipeline>().RunAsync(euCts.Token);
}
if (args.Contains("--uk"))
{
    using var ukCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ukCts.Cancel(); };
    return await host.Services.GetRequiredService<CompanyPaisa.Importer.Uk.UkImportPipeline>().RunAsync(args.Contains("--refresh-uk-list"), ukCts.Token);
}

// Diagnostics: dotnet run --project tools/CompanyPaisa.Importer -- --debug-proxy <filing url>
if (args is ["--debug-proxy", var url])
{
    var html = await host.Services.GetRequiredService<ISecClient>().GetStringAsync(url, CachePolicy.Immutable) ?? "";
    foreach (var line in SummaryCompensationTableParser.Describe(html)) Console.WriteLine(line);
    foreach (var row in new SummaryCompensationTableParser().Parse(html).Rows)
        Console.WriteLine($"{row.Name} | {row.Title} | {row.Year} | sal {row.Salary:N0} bonus {row.Bonus:N0} stock {row.StockAwards:N0} other {row.Other:N0} total {row.Total:N0} {(row.ComponentsVerified ? "✓" : "✗")}");
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
return await host.Services.GetRequiredService<ImportPipeline>().RunAsync(cts.Token);
