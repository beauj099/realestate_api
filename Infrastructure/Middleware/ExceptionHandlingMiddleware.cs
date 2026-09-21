using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace RealEstateApi.Infrastructure.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://httpstatuses.io/404",
                Status = StatusCodes.Status404NotFound,
                Title = "Not Found",
                Detail = ex.Message
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            _logger.LogWarning(ex, "Unique constraint violation");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://httpstatuses.io/409",
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "The resource already exists or violates a uniqueness constraint"
            });
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            _logger.LogWarning(ex, "Foreign key constraint violation: {Message}", ex.Message);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://httpstatuses.io/400",
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad Request",
                Detail = $"The operation violates a data integrity constraint: {ex.Message}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://httpstatuses.io/500",
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred"
            });
        }
    }
}
