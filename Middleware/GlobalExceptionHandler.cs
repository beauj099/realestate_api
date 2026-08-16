using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RealEstateApi.Middleware;

/// <summary>
/// Translates unhandled exceptions into consistent ProblemDetails responses so that,
/// e.g., a KeyNotFoundException thrown by a service becomes a 404 instead of a bare 500.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");
        else
            _logger.LogWarning(exception, "{Title}", title);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            // Do not leak internal details for 500s; client errors carry a safe message.
            Detail = statusCode == StatusCodes.Status500InternalServerError ? null : exception.Message
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
