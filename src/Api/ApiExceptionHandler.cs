using Application;
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
        switch (exception)
        {
            case DomainValidationException validationException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    validationException.Code,
                    validationException.Message);
                return true;
            case NotFoundException notFoundException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status404NotFound,
                    "NotFound",
                    notFoundException.Message);
                return true;
            case ForbiddenException forbiddenException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status403Forbidden,
                    forbiddenException.Code,
                    forbiddenException.Message);
                return true;
            case ConflictException conflictException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    conflictException.Code,
                    conflictException.Message);
                return true;
            case IntegrationUnavailableException integrationException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status503ServiceUnavailable,
                    "IntegrationUnavailable",
                    integrationException.Message);
                return true;
            case BadHttpRequestException:
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    "InvalidRequest",
                    "Проверьте поля запроса.");
                return true;
            default:
                logger.LogError("Unhandled API error. TraceId: {TraceId}", httpContext.TraceIdentifier);
                await ApiError.WriteAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "InternalError",
                    "Внутренняя ошибка сервера.");
                return true;
        }
    }
}
