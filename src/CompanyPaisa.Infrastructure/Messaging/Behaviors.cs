using System.Diagnostics;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyPaisa.Infrastructure.Messaging;

/// <summary>Logs every request with its duration; warns when it's slower than configured.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    IOptionsMonitor<PipelineOptions> options) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            var ms = watch.ElapsedMilliseconds;
            if (ms > options.CurrentValue.SlowRequestThresholdMs)
                logger.LogWarning("{Request} took {ElapsedMs} ms (slow threshold {Threshold} ms)", name, ms, options.CurrentValue.SlowRequestThresholdMs);
            else
                logger.LogDebug("{Request} handled in {ElapsedMs} ms", name, ms);
            return response;
        }
        catch (Exception ex) when (ex is not RequestValidationException and not NotFoundException and not OperationCanceledException)
        {
            logger.LogError(ex, "{Request} failed after {ElapsedMs} ms", name, watch.ElapsedMilliseconds);
            throw;
        }
    }
}

/// <summary>Runs every <see cref="IRequestValidator{TRequest}"/> for the request; throws if any report errors.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IRequestValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        return errors.Count > 0 ? throw new RequestValidationException(errors) : next();
    }
}

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
