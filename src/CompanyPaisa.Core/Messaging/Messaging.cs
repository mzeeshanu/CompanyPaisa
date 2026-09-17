namespace CompanyPaisa.Core.Messaging;

/// <summary>Marker for a request (query or command) that produces a <typeparamref name="TResponse"/>.</summary>
public interface IRequest<TResponse>;

/// <summary>Handles exactly one request type. One use case = one request + one handler.</summary>
public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}

/// <summary>The next step in the pipeline (another behavior, or the handler itself).</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Cross-cutting step that wraps every handler: logging, validation, caching…
/// Behaviors run in registration order.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>
/// Single entry point for executing use cases. Endpoints (and anything else) call
/// <c>requestor.SendAsync(new SomeQuery(...))</c> and never talk to handlers directly.
/// </summary>
public interface IServiceRequestor
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}

/// <summary>Validates a request before its handler runs. Register as many as needed per request type.</summary>
public interface IRequestValidator<in TRequest>
{
    IEnumerable<ValidationError> Validate(TRequest request);
}

public sealed record ValidationError(string Field, string Message);

/// <summary>Opt-in: requests implementing this are recorded by the analytics behavior when they succeed.</summary>
public interface ITrackedRequest<in TResponse>
{
    /// <summary>What to record for this request and its response; null = nothing (e.g. a later page of results).</summary>
    Abstractions.AnalyticsAction? Describe(TResponse response);
}

/// <summary>Opt-in: requests implementing this are cached by the caching behavior.</summary>
public interface ICacheableRequest
{
    /// <summary>Unique key for this request's parameters.</summary>
    string CacheKey { get; }
    /// <summary>Name of the cache profile in configuration (Caching:Profiles).</summary>
    string CacheProfile { get; }
}
