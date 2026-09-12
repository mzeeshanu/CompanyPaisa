using CompanyPaisa.Core;
using CompanyPaisa.Data.Excel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CompanyPaisa.Api.Errors;

/// <summary>Maps known exceptions to RFC 7807 problem responses; everything else is a 500 without internals.</summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            RequestValidationException v => new HttpValidationProblemDetails(
                v.Errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request has invalid values."
            },
            NotFoundException nf => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = nf.Message },
            DataLoadException or FileNotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Data is unavailable",
                Detail = "The company data couldn't be loaded. The team has been notified."
            },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Something went wrong" }
        };

        if (problem.Status >= 500) logger.LogError(exception, "Unhandled error for {Path}", context.Request.Path);

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
