using CompanyPaisa.Core.Messaging;

namespace CompanyPaisa.Core;

/// <summary>Thrown when a request is invalid. Mapped to HTTP 400 by the API.</summary>
public sealed class RequestValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("The request is invalid: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>Thrown when something asked for doesn't exist. Mapped to HTTP 404 by the API.</summary>
public sealed class NotFoundException(string message) : Exception(message);
