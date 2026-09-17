using CompanyPaisa.Core;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Infrastructure.Geo;
using CompanyPaisa.Infrastructure.Messaging;
using CompanyPaisa.Infrastructure.Options;
using CompanyPaisa.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the request pipeline (requestor + behaviors), caching, clock, path resolution and ZIP lookup.
    /// Behavior order: logging → analytics → validation → caching → handler (analytics sits outside the cache, so a cached
    /// answer still counts as a visit to that company).
    /// </summary>
    public static IServiceCollection AddCompanyPaisaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<CachingOptions>(configuration, CachingOptions.SectionName);
        services.AddValidatedOptions<PipelineOptions>(configuration, PipelineOptions.SectionName);

        services.AddMemoryCache();
        services.AddSingleton<IConfigureOptions<Microsoft.Extensions.Caching.Memory.MemoryCacheOptions>>(sp =>
            new ConfigureOptions<Microsoft.Extensions.Caching.Memory.MemoryCacheOptions>(o =>
                o.SizeLimit = sp.GetRequiredService<IOptions<CachingOptions>>().Value.MaxEntries));

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IFilePathResolver, ContentRootPathResolver>();
        services.TryAddSingleton<IDataChangeSignal, DataChangeSignal>();
        services.TryAddSingleton<CsvZipGeoLocator>();
        services.TryAddSingleton<IGeoLocator>(sp => sp.GetRequiredService<CsvZipGeoLocator>());
        services.TryAddSingleton<IReverseGeoLocator>(sp => sp.GetRequiredService<CsvZipGeoLocator>());

        services.TryAddScoped<IServiceRequestor, ServiceRequestor>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AnalyticsBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        return services;
    }
}
