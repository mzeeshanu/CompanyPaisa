using System.Diagnostics;
using CompanyPaisa.Core;
using CompanyPaisa.Core.Messaging;
using CompanyPaisa.Infrastructure.Options;
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
