using CompanyPaisa.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Infrastructure.Messaging;

/// <summary>
/// The cache <see cref="CachingBehavior{TRequest,TResponse}"/> keeps query results in, and nothing else. It is its own
/// instance because its size limit (Caching:MaxEntries) counts rows (<see cref="CacheWeight"/>): sharing it with things
/// measured in other units let one large entry (the sitemap) be refused every time and push query results out.
/// The app's general <see cref="IMemoryCache"/> is a separate cache.
/// </summary>
public sealed class QueryResultCache(IOptions<CachingOptions> options)
    : MemoryCache(Microsoft.Extensions.Options.Options.Create(new MemoryCacheOptions { SizeLimit = options.Value.MaxEntries }))
{
    public long SizeLimit { get; } = options.Value.MaxEntries;
}
