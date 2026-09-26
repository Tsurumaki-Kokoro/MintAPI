using HitCircleAPI.Middleware;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitCircleAPI.Tests.Unit;

public class RetryableExceptionHandlerTests
{
    [Theory]
    [InlineData(typeof(RenderBusyException))]
    [InlineData(typeof(RenderTimeoutException))]
    [InlineData(typeof(OsuQuotaExceededException))]
    public async Task TryHandleAsync_maps_retryable_exceptions_to_503_with_RetryAfter(Type exceptionType)
    {
        var context = NewContext();

        var handled = await Handle(context, (Exception)Activator.CreateInstance(exceptionType)!);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("5", context.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task TryHandleAsync_leaves_other_exceptions_alone()
    {
        var context = NewContext();

        var handled = await Handle(context, new InvalidOperationException("boom"));

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static Task<bool> Handle(HttpContext context, Exception exception)
        => new RetryableExceptionHandler(NullLogger<RetryableExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None)
            .AsTask();

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
