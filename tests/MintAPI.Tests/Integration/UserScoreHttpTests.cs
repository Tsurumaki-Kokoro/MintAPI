using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using MintAPI.Errors;
using System.Net.Http.Json;

namespace MintAPI.Tests.Integration;

public sealed class UserScoreHttpTests
{
    [Fact]
    public async Task Invalid_query_returns_original_error_instead_of_406()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>()).AddApplicationPart(typeof(ScoreController).Assembly).AddApplicationPart(typeof(ErrorProbeController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new ScoreController(null!, null!, null!, null!, null!, NullLogger<ScoreController>.Instance));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await http.GetAsync("/score/user_score?platform=Test&platform_uid=123&beatmap_id=1949106&theme=invalid");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            Assert.Equal("INVALID_ARGUMENT", error!.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
            Assert.Contains("theme", await response.Content.ReadAsStringAsync());
            foreach (var path in new[] { "/error-probe/failure", "/error-probe/exception" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Accept.ParseAdd("image/png");
                using var failed = await http.SendAsync(request);
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal("application/problem+json", failed.Content.Headers.ContentType!.MediaType);
                var failure = await failed.Content.ReadFromJsonAsync<ApiError>();
                Assert.Equal("INTERNAL_ERROR", failure!.Code);
                Assert.NotEmpty(failure.TraceId);
                Assert.DoesNotContain("private", failure.Message);
            }
            using var local = await http.GetAsync("/error-probe/local");
            Assert.Equal(HttpStatusCode.NotFound, local.StatusCode);
            Assert.Equal("LOCAL_SCORE_NOT_COLLECTED", (await local.Content.ReadFromJsonAsync<ApiError>())!.Code);
        }
        finally { await app.StopAsync(); }
    }
}

[ApiController]
[Route("error-probe")]
public sealed class ErrorProbeController : ControllerBase
{
    [HttpGet("failure")]
    [Produces("image/png")]
    public IActionResult Failure() => StatusCode(500, "private internal details");

    [HttpGet("exception")]
    [Produces("image/png")]
    public IActionResult Exception() => throw new InvalidOperationException("private exception details");

    [HttpGet("local")]
    [Produces("image/png")]
    public IActionResult Local() => ApiErrors.Result(ErrorCatalog.LocalScoreNotCollected);
}
