using System.Reflection;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Core.Options;
using CompanyPaisa.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompanyPaisa.Core;

public static class DependencyInjection
{
    /// <summary>Registers domain services, handlers, validators and the options they read.</summary>
    public static IServiceCollection AddCompanyPaisaCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<SearchOptions>(configuration, SearchOptions.SectionName)
            .PostConfigure(o => { if (o.AllowedRadiiMiles.Count == 0) o.AllowedRadiiMiles.AddRange(SearchOptions.DefaultAllowedRadii); });
        services.AddValidatedOptions<MetricsOptions>(configuration, MetricsOptions.SectionName);
        services.AddValidatedOptions<GeoOptions>(configuration, GeoOptions.SectionName);
        services.AddValidatedOptions<CurrencyOptions>(configuration, CurrencyOptions.SectionName);
        services.AddValidatedOptions<BenchmarkOptions>(configuration, BenchmarkOptions.SectionName);
        services.TryAddSingleton<ICurrencyConverter, CurrencyConverter>();

        services.TryAddSingleton<IDistanceCalculator, HaversineDistanceCalculator>();
        services.TryAddSingleton<IFinancialMetricsService, FinancialMetricsService>();
        services.TryAddSingleton<INearbySearchService, NearbySearchService>();
        services.TryAddSingleton<ITopPaidCeoService, TopPaidCeoService>();
        services.TryAddSingleton<INameSearchIndex, NameSearchIndex>();
        services.TryAddSingleton<ICompanyStatsIndex, CompanyStatsIndex>();
        // Analytics are off unless the host registers a real tracker (the website does; the last registration wins).
        services.TryAddSingleton<IAnalyticsTracker, NullAnalyticsTracker>();

        services.AddRequestHandlersFrom(typeof(DependencyInjection).Assembly);
        return services;
    }

    /// <summary>Binds a section to a typed options class and fails fast at startup if it's invalid.</summary>
    public static Microsoft.Extensions.Options.OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

    /// <summary>Registers every IRequestHandler and IRequestValidator implementation found in the assembly.</summary>
    public static IServiceCollection AddRequestHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var openTypes = new[] { typeof(IRequestHandler<,>), typeof(IRequestValidator<>) };
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType && openTypes.Contains(i.GetGenericTypeDefinition())))
            services.AddTransient(iface, type);
        return services;
    }
}
