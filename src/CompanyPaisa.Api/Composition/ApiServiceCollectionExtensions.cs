using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CompanyPaisa.Analytics;
using CompanyPaisa.Api.Analytics;
using CompanyPaisa.Api.Endpoints;
using CompanyPaisa.Api.Errors;
using CompanyPaisa.Api.Options;
using CompanyPaisa.Api.Security;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Data.Excel;
using CompanyPaisa.Data.Sqlite;
using CompanyPaisa.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Api.Composition;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "external-origins";

    /// <summary>Composition root: wires every layer together from configuration.</summary>
    public static IServiceCollection AddCompanyPaisa(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCompanyPaisaCore(configuration);
        services.AddCompanyPaisaInfrastructure(configuration);
        services.AddCompanyPaisaDataSource(configuration);
        services.AddCompanyPaisaApi(configuration);
        services.AddCompanyPaisaWebAnalytics(configuration);
        services.AddSingleton<IndexHtml>();
        services.AddScoped<PageRenderer>();
        return services;
    }

    /// <summary>Picks the <see cref="ICompanyRepository"/> implementation named in DataSource:Provider.</summary>
    public static IServiceCollection AddCompanyPaisaDataSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<DataSourceOptions>(configuration, DataSourceOptions.SectionName);
        var provider = configuration.GetSection(DataSourceOptions.SectionName).Get<DataSourceOptions>()?.Provider ?? "Sqlite";

        return provider.ToLowerInvariant() switch
        {
            "sqlite" => services.AddSqliteDataSource(configuration),
            "excel" => services.AddExcelDataSource(configuration),
            _ => throw new InvalidOperationException(
                $"DataSource:Provider '{provider}' isn't supported. Supported: Sqlite, Excel.")
        };
    }

    /// <summary>
    /// The site's own visitor analytics (Analytics:Provider). When a store is configured, the tracker that reads visitor details
    /// from each request replaces Core's do-nothing one; otherwise nothing is recorded.
    /// </summary>
    public static IServiceCollection AddCompanyPaisaWebAnalytics(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.AddCompanyPaisaAnalytics(configuration).Enabled)
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<HttpAnalyticsTracker>();
            services.AddSingleton<IAnalyticsTracker>(sp => sp.GetRequiredService<HttpAnalyticsTracker>());
        }
        return services;
    }

    private static void AddCompanyPaisaApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ApiOptions>(configuration, ApiOptions.SectionName);
        services.AddValidatedOptions<UiOptions>(configuration, UiOptions.SectionName);
        services.AddOptions<FeatureOptions>().Bind(configuration.GetSection(FeatureOptions.SectionName));

        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();
        services.AddScoped<ApiKeyEndpointFilter>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SiteSessions>();

        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        services.AddOpenApi();

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Hourly caps per visitor IP on top of the per-minute one: API calls without a key, and server-rendered pages.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? "";
                var limits = context.RequestServices.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue.RateLimits;
                int? perHour = null;
                string? partition = null;
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    var caller = context.RequestServices.GetRequiredService<IApiKeyValidator>().Identify(context);
                    if (!caller.IsKeyed) (perHour, partition) = (limits.AnonymousPerHour, "api-hour:" + caller.PartitionKey);
                }
                else if (SitePages.IsRenderedPage(path))
                {
                    var header = context.RequestServices.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue.ClientIpHeader;
                    (perHour, partition) = (limits.PagesPerHour, "pages-hour:" + ApiKeyValidator.ClientIp(context, header));
                }
                return perHour is { } permits
                    ? RateLimitPartition.GetFixedWindowLimiter(partition!, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permits, Window = TimeSpan.FromHours(1), QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter("unlimited");
            });
            o.AddPolicy(V1Endpoints.RateLimitPolicy, context =>
            {
                var caller = context.RequestServices.GetRequiredService<IApiKeyValidator>().Identify(context);
                var limits = context.RequestServices.GetRequiredService<IOptionsMonitor<ApiOptions>>().CurrentValue.RateLimits;
                return RateLimitPartition.GetFixedWindowLimiter(caller.PartitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = caller.IsKeyed ? limits.KeyedPerMinute : limits.AnonymousPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
        });

        var origins = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()?.CorsAllowedOrigins ?? [];
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.WithOrigins([.. origins]).WithMethods("GET").AllowAnyHeader()));

        services.AddHealthChecks().AddCheck<DataSourceHealthCheck>("data-source");
    }
}

/// <summary>Healthy when the data source loads and has at least one company.</summary>
public sealed class DataSourceHealthCheck(ICompanyRepository repository) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var (companies, locations) = await repository.GetCountsAsync(ct);
            var meta = await repository.GetMetadataAsync(ct);
            var data = new Dictionary<string, object> { ["companies"] = companies, ["locations"] = locations, ["version"] = meta.DataVersion, ["sample"] = meta.IsSampleData };
            return companies > 0 ? HealthCheckResult.Healthy("Data loaded", data) : HealthCheckResult.Degraded("No companies loaded", data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Data source failed to load", ex);
        }
    }
}
