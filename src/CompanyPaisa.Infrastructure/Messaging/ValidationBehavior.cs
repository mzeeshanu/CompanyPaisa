using CompanyPaisa.Core;
using CompanyPaisa.Core.Messaging;

namespace CompanyPaisa.Infrastructure.Messaging;

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
