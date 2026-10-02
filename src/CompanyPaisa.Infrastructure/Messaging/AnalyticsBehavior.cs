using CompanyPaisa.Core.Abstractions;
using CompanyPaisa.Core.Messaging;
using Microsoft.Extensions.Logging;

namespace CompanyPaisa.Infrastructure.Messaging;

/// <summary>
/// Records requests that implement <see cref="ITrackedRequest{TResponse}"/> once they succeed (failed and invalid requests aren't
/// visits). Recording must never break the request, so problems are logged and swallowed.
/// </summary>
public sealed class AnalyticsBehavior<TRequest, TResponse>(IAnalyticsTracker tracker, ILogger<AnalyticsBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await next();
        if (request is ITrackedRequest<TResponse> tracked)
        {
            try
            {
                if (tracked.Describe(response) is { } action) tracker.Track(action);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't record analytics for {Request}", typeof(TRequest).Name);
            }
        }
        return response;
    }
}
