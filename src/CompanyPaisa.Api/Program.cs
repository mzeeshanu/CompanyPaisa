using CompanyPaisa.Api.Analytics;
using CompanyPaisa.Api.Composition;
using CompanyPaisa.Api.Endpoints;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Api.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Hosting platforms (Railway, Render, Heroku…) tell the app which port to listen on via PORT.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://*:{port}");   // all interfaces, IPv4 and IPv6 (Railway's private network is IPv6)
}

builder.Services.AddCompanyPaisa(builder.Configuration);

// Cloudflare's visitor location headers can hold non-ASCII place names ("Zürich"); read those as UTF-8.
builder.WebHost.ConfigureKestrel(k => k.RequestHeaderEncodingSelector = name =>
    name.StartsWith("cf-", StringComparison.OrdinalIgnoreCase) ? System.Text.Encoding.UTF8 : null);

var hosting = builder.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
if (hosting.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The platform's proxy addresses aren't known in advance.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

var app = builder.Build();
var api = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;

if (hosting.TrustForwardedHeaders) app.UseForwardedHeaders();   // must run first
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    // Not in development: the React dev server calls the API over plain http://localhost:5104.
    if (hosting.UseHttpsRedirection) app.UseHttpsRedirection();
}

// Every page the site serves hands out the website's pass to the API (see SiteSessions).
app.UseSiteSessions();
// The React build is copied into wwwroot at publish time; serve it when present. The home page isn't a static file:
// MapCompanyPaisaPages serves it with its content.
app.UseStaticFiles();

app.UseCors(ApiServiceCollectionExtensions.CorsPolicy);
app.UseRateLimiter();

if (api.EnableOpenApi)
{
    app.MapOpenApi();                       // /openapi/v1.json
    if (api.EnableSwaggerUi)
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/openapi/v1.json", "CompanyPaisa API v1");
            o.DocumentTitle = "CompanyPaisa API";
        });                                 // /swagger
}

app.MapCompanyPaisaApiV1();
app.MapCompanyPaisaAnalytics();
app.MapCompanyPaisaPages();         // /, /company/{ticker}, /executive/{personId}, /near/…: the app's HTML with the page's title and content
app.MapSiteSession();               // /api/session: a fresh pass for a page left open
app.MapHealthChecks("/health");

// Client-side routes of the React app (anything that isn't an API, docs, health or a real file).
app.MapFallbackToFile("{*path:regex(^(?!api/|openapi/|swagger|health|assets/).*$)}", "index.html");

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;

