using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Infrastructure.Messaging;

/// <summary>
/// Caches responses of requests that implement <see cref="ICacheableRequest"/>, using the
/// duration of their cache profile from configuration. Keys include the data generation,
/// so reloading data invalidates the cache.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    IMemoryCache cache,
    IDataChangeSignal dataChange,
    IOptionsMonitor<CachingOptions> options) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (!o.Enabled || request is not ICacheableRequest cacheable ||
            !o.Profiles.TryGetValue(cacheable.CacheProfile, out var seconds) || seconds <= 0)
            return await next();

        var key = $"{dataChange.Generation}|{typeof(TRequest).Name}|{cacheable.CacheKey}";
        if (cache.TryGetValue(key, out TResponse? cached) && cached is not null) return cached;

        var response = await next();
        cache.Set(key, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(seconds),
            Size = CacheWeight.Of(response)
        });
        return response;
    }
}

/// <summary>
/// Cache "size" in rows, so Caching:MaxEntries bounds memory: a 500-company search costs 500, a company profile 1.
/// (Counting every entry as 1 let 10,000 large searches pile up.)
/// </summary>
public static class CacheWeight
{
    public static long Of(object? response) => response switch
    {
        Contracts.NearbyCompaniesResponse r => Math.Max(1, r.Items.Count),
        Contracts.ExecutivesNearResponse r => Math.Max(1, r.Items.Count),
        System.Collections.ICollection c => Math.Max(1, c.Count),
        _ => 1
    };
}
