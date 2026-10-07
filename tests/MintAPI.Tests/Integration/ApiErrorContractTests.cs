using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintAPI.Errors;
using MintAPI.Middleware;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class ApiErrorContractTests
{
    [Theory]
    [InlineData("business", "USER_NOT_BOUND", 404, LogLevel.Information)]
    [InlineData("failure", "INTERNAL_ERROR", 500, LogLevel.Error)]
    [InlineData("exception", "INTERNAL_ERROR", 500, LogLevel.Error)]
    [InlineData("upstream", "OSU_API_UNAVAILABLE", 502, LogLevel.Error)]
    [InlineData("busy", "SERVICE_BUSY", 503, LogLevel.Warning)]
    [InlineData("validation?count=invalid", "INVALID_ARGUMENT", 400, LogLevel.Information)]
    public async Task Errors_obey_http_and_log_contract(string path, string code, int status, LogLevel level)
    {
        var logs = new RecordingLoggerProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(ApiErrorContractProbeController).Assembly);
        builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api-error-contract/" + path);
            request.Headers.Accept.ParseAdd("image/png");
            request.Headers.Add("access_token", "private-token-value");
            using var response = await http.SendAsync(request);
            var error = await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-diagnostic", "private-token-value");
            var record = Assert.Single(logs.Entries, e => e.Message.Contains(code) && e.Message.Contains(error.TraceId) && e.Level == level);
            Assert.DoesNotContain("private-token-value", record.Message);
            if (path is "exception" or "upstream")
            {
                Assert.NotNull(record.Exception);
                Assert.Contains("private-diagnostic", record.Exception.Message);
                Assert.NotEmpty(record.Exception.StackTrace!);
            }
            if (path == "failure") Assert.Contains("private-diagnostic", record.Message);
            if (path == "busy") Assert.Equal("5", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        }
        finally { await app.StopAsync(); }
    }
}

[ApiController]
[Route("api-error-contract")]
[Produces("image/png")]
public sealed class ApiErrorContractProbeController : ControllerBase
{
    [HttpGet("business")]
    public IActionResult Business() => ApiErrors.Result(ErrorCatalog.UserNotBound);
    [HttpGet("failure")]
    public IActionResult Failure() => ApiErrors.Result(ErrorCatalog.InternalError, message: "private-diagnostic", diagnostic: "private-diagnostic");
    [HttpGet("exception")]
    public IActionResult FailureException() => throw new InvalidOperationException("private-diagnostic");
    [HttpGet("upstream")]
    public IActionResult Upstream() => throw new HttpRequestException("private-diagnostic");
    [HttpGet("busy")]
    public IActionResult Busy() => throw new RenderBusyException();
    [HttpGet("validation")]
    public IActionResult Validation([FromQuery] int count) => Ok(count);
}
