using System.Net;
using HitCircleAPI.Controllers;
using HitCircleAPI.Rendering.MultiplayerTheme;
using HitCircleAPI.Services;
using HitCircleAPI.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitCircleAPI.Tests.Unit;

public class MultiplayerControllerTests
{
    [Theory]
    [InlineData(null, "apple", 1)]
    [InlineData(-1, "apple", 1)]
    [InlineData(1, "unknown", 1)]
    [InlineData(1, "apple", 0)]
    public async Task Invalid_history_parameters_do_not_call_upstream(int? id, string theme, int page)
    {
        var api = new RecordingOsuApiService();
        Assert.IsType<BadRequestObjectResult>(await Create(api).GetMatchHistory(id, theme, page));
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Invalid_rating_algorithm_does_not_call_upstream()
    {
        var api = new RecordingOsuApiService();
        Assert.IsType<BadRequestObjectResult>(await Create(api).GetRating(1, "unsupported"));
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task History_returns_png_and_page_headers()
    {
        var api = new RecordingOsuApiService { MatchHandler = _ => MultiplayerDataTests.Sample(rounds: 20) };
        var controller = Create(api);
        var result = Assert.IsType<FileContentResult>(await controller.GetMatchHistory(12345, "apple", 2));
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("2", controller.Response.Headers["X-Page-Count"].ToString());
        Assert.Equal("2", controller.Response.Headers["X-Page"].ToString());
        Assert.IsType<BadRequestObjectResult>(await controller.GetMatchHistory(12345, "apple", 3));
    }

    [Fact]
    public async Task Rating_returns_png_with_case_insensitive_algorithm()
    {
        var api = new RecordingOsuApiService { MatchHandler = _ => MultiplayerDataTests.Sample(false) };
        var result = Assert.IsType<FileContentResult>(await Create(api).GetRating(12345, "FLASHLIGHT", "default"));
        Assert.Equal("image/png", result.ContentType);
    }

    [Fact]
    public async Task Upstream_not_found_is_mapped_to_404()
    {
        var api = new RecordingOsuApiService { MatchHandler = _ => throw new HttpRequestException("Missing", null, HttpStatusCode.NotFound) };
        Assert.IsType<NotFoundObjectResult>(await Create(api).GetMatchHistory(12345));
    }

    [Fact]
    public async Task Retryable_errors_remain_available_to_global_handler()
    {
        var api = new RecordingOsuApiService { MatchHandler = _ => throw new OsuQuotaExceededException() };
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => Create(api).GetMatchHistory(12345));
    }

    private static MultiplayerController Create(RecordingOsuApiService api)
    {
        var theme = new MultiplayerTheme(new StubRenderer(), new StubImages(), NullLogger<MultiplayerTheme>.Instance);
        return new MultiplayerController(new MultiplayerService(api), theme, NullLogger<MultiplayerController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class StubRenderer : IRenderService
    {
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult(new byte[] { 0x89, 0x50, 0x4e, 0x47 });
    }

    private sealed class StubImages : IImageCacheService
    {
        public Task<byte[]> GetAvatarAsync(string avatarUrl, int userId) => throw new IOException("Offline");
        public Task<byte[]?> GetUserBackgroundAsync(int userId) => throw new NotSupportedException();
        public Task SaveUserBackgroundAsync(int userId, byte[] data) => throw new NotSupportedException();
        public Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index) => throw new NotSupportedException();
    }
}
