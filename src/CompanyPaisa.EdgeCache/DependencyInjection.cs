using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.EdgeCache;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the adapter named in EdgeCache:Provider (from the ones <paramref name="adapters"/> adds) and the purge after
    /// each start. An adapter whose settings are incomplete (Cloudflare without a zone ID or token) logs a warning at startup
    /// and purges nothing; an unknown provider name stops startup. The returned status says which.
    /// </summary>
    public static EdgeCacheStatus AddCompanyPaisaEdgeCache(this IServiceCollection services, IConfiguration configuration,
        Action<EdgeCacheAdapters> adapters)
    {
        var section = configuration.GetSection(EdgeCacheOptions.SectionName);
        services.AddOptions<EdgeCacheOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var o = section.Get<EdgeCacheOptions>() ?? new EdgeCacheOptions();

        var registry = new EdgeCacheAdapters();
        adapters(registry);

        var status = registry.Status(o.Provider, section);
        services.AddSingleton(status);
        var adapter = registry.Get(o.Provider);
        if (adapter is null) return status;   // None: nothing to purge, nothing to say

        if (status.Enabled) services.AddSingleton(sp => adapter.Create(sp, section));
        services.AddHostedService(sp => new EdgeCachePurgeOnStart(sp.GetService<IEdgeCache>(), status,
            sp.GetRequiredService<IOptions<EdgeCacheOptions>>(), sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<ILogger<EdgeCachePurgeOnStart>>()));
        return status;
    }
}
