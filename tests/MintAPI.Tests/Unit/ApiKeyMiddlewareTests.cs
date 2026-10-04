using MintAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace MintAPI.Tests.Unit;

public class ApiKeyMiddlewareTests
{
    private static IConfiguration Config(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApiKey"] = apiKey })
            .Build();

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection([]).Build();

    // 这三条是核心：ApiKey 缺失/为空时必须拒绝启动，否则 `string.Equals(header, "")`
    // 会让一个空值头通过鉴权 —— 整个 API 裸奔。
    [Fact]
    public void Constructor_throws_when_ApiKey_is_absent()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ApiKeyMiddleware(_ => Task.CompletedTask, EmptyConfig()));

        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void Constructor_throws_when_ApiKey_is_empty()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ApiKeyMiddleware(_ => Task.CompletedTask, Config("")));

        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void Constructor_throws_when_ApiKey_is_whitespace()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ApiKeyMiddleware(_ => Task.CompletedTask, Config("   ")));

        Assert.Contains("ApiKey", ex.Message);
    }

    // 下面四条是重构鉴权比较路径的回归守卫：它们在改动前就已通过，
    // 作用是证明这次收敛没有把鉴权改坏。
    private static async Task<int> RunAsync(string configuredKey, string? headerValue)
    {
        var middleware = new ApiKeyMiddleware(_ => Task.CompletedTask, Config(configuredKey));
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers["access_token"] = headerValue;
        }

        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task InvokeAsync_rejects_an_empty_header_value()
    {
        var status = await RunAsync(configuredKey: "s3cret", headerValue: "");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task InvokeAsync_rejects_a_request_without_the_header()
    {
        var status = await RunAsync(configuredKey: "s3cret", headerValue: null);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task InvokeAsync_rejects_a_wrong_key()
    {
        var status = await RunAsync(configuredKey: "s3cret", headerValue: "wrong");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task InvokeAsync_lets_the_configured_key_through()
    {
        var status = await RunAsync(configuredKey: "s3cret", headerValue: "s3cret");

        Assert.NotEqual(StatusCodes.Status403Forbidden, status);
    }
}
