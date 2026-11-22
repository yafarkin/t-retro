using System.Diagnostics;
using Api.Contracts;

namespace Api;

public static class ApiError
{
    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message)
    {
        context.Response.StatusCode = statusCode;
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(new ErrorResponse(code, message, traceId));
    }
}
