using HitCircleAPI.Services;
using Microsoft.AspNetCore.Diagnostics;

namespace HitCircleAPI.Middleware;

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

        logger.LogWarning(retryable, "请求被拒（资源繁忙）：{Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(
            new { error = retryable.Message, retry_after = RetryAfterSeconds },
            cancellationToken);

        return true;
    }
}
