using Microsoft.AspNetCore.Diagnostics;
using MintAPI.Services;
using MintAPI.Errors;

namespace MintAPI.Middleware;

/// <summary>将可重试异常映射为 503 和 Retry-After。</summary>
public sealed class RetryableExceptionHandler(
    ILogger<RetryableExceptionHandler> logger) : IExceptionHandler
{
    private const int RetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not RetryableException retryable)
            return false;

        var error = ApiErrors.Create(httpContext, ErrorCatalog.ServiceBusy);
        logger.LogWarning(retryable, "API retryable {Code} {Status} {Method} {Path} {TraceId} {RequestContext}",
            error.Code, error.Status, httpContext.Request.Method, httpContext.Request.Path, error.TraceId, ApiErrors.Context(httpContext));

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(
            error, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }
}
