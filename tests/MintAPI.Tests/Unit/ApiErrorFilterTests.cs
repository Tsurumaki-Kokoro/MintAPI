using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Errors;

namespace MintAPI.Tests.Unit;

public sealed class ApiErrorFilterTests
{
    [Theory]
    [InlineData(404, "User not found", 404, "USER_NOT_BOUND")]
    [InlineData(404, "No recent play record found", 404, "RECENT_PLAY_NOT_FOUND")]
    [InlineData(404, "No best play record found", 404, "BEST_PLAY_NOT_FOUND")]
    [InlineData(500, "Score has no beatmap info", 500, "INTERNAL_ERROR")]
    [InlineData(500, "Failed to render score image", 500, "RENDER_FAILED")]
    [InlineData(400, "Failed to get user score: secret internal details", 502, "OSU_API_UNAVAILABLE")]
    public async Task Errors_keep_business_codes_and_do_not_expose_internal_details(int status, string reason, int expectedStatus, string code)
    {
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(action, [], new ObjectResult(reason) { StatusCode = status }, new object());
        var filter = new ApiErrorFilter(NullLogger<ApiErrorFilter>.Instance);
        await filter.OnResultExecutionAsync(context, () => Task.FromResult(new ResultExecutedContext(action, [], context.Result, new object())));
        var result = Assert.IsType<JsonResult>(context.Result);
        var error = Assert.IsType<ApiError>(result.Value);
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(code, error.Code);
        Assert.Equal("application/problem+json", result.ContentType);
        Assert.NotEmpty(error.TraceId);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("BEATMAP_NOT_FOUND")]
    [InlineData("SCORE_NOT_FOUND")]
    public async Task Lookup_errors_keep_the_resource_code(string code)
    {
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        var context = new ExceptionContext(action, []) { Exception = new ApiLookupException(new ErrorDefinition(404, code, "查无记录"), new HttpRequestException()) };
        await new ApiErrorFilter(NullLogger<ApiErrorFilter>.Instance).OnExceptionAsync(context);
        Assert.True(context.ExceptionHandled);
        var error = Assert.IsType<ApiError>(Assert.IsType<JsonResult>(context.Result).Value);
        Assert.Equal(404, error.Status);
        Assert.Equal(code, error.Code);
    }
}
