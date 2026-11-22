using Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DomainValidationException validationException)
        {
            await ApiError.WriteAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                validationException.Code,
                validationException.Message);
            return true;
        }

        logger.LogError("Unhandled API error. TraceId: {TraceId}", httpContext.TraceIdentifier);
        await ApiError.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "InternalError",
            "Внутренняя ошибка сервера.");
        return true;
    }
}
