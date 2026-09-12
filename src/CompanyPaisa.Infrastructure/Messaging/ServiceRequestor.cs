using System.Collections.Concurrent;
using CompanyPaisa.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyPaisa.Infrastructure.Messaging;

/// <summary>
/// Finds the handler for a request, wraps it in the registered pipeline behaviors and runs it.
/// Generic plumbing is built once per request type and cached.
/// </summary>
public sealed class ServiceRequestor(IServiceProvider services) : IServiceRequestor
{
    private static readonly ConcurrentDictionary<Type, object> Pipelines = new();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pipeline = (Pipeline<TResponse>)Pipelines.GetOrAdd(request.GetType(), static t =>
            Activator.CreateInstance(typeof(Pipeline<,>).MakeGenericType(t, typeof(TResponse)))!);
        return pipeline.RunAsync(request, services, ct);
    }

    private abstract class Pipeline<TResponse>
    {
        public abstract Task<TResponse> RunAsync(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class Pipeline<TRequest, TResponse> : Pipeline<TResponse> where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> RunAsync(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct)
        {
            var typed = (TRequest)request;
            var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
                          ?? throw new InvalidOperationException($"No handler is registered for {typeof(TRequest).Name}.");

            RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(typed, ct);

            // Wrap from the inside out so the first registered behavior runs first.
            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var inner = next;
                next = () => behavior.HandleAsync(typed, inner, ct);
            }

            return next();
        }
    }
}
