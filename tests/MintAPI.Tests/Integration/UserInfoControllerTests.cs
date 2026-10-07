using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Rendering.UserInfoTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class UserInfoControllerTests
{
    [Theory]
    [InlineData(" Player Name ", null, null, null, "Player Name", 0)]
    [InlineData("Player Name", "qq", "missing", null, "Player Name", 0)]
    [InlineData("Player Name", "qq", "100", null, "Player Name", 3)]
    [InlineData("Player Name", "qq", "100", 1, "Player Name", 1)]
    [InlineData(null, "qq", "100", null, "123", 3)]
    [InlineData(null, "qq", "100", 0, "123", 0)]
    public async Task Query_resolves_direct_username_or_binding_and_mode(string? username, string? platform,
        string? uid, int? mode, string expectedTarget, int expectedMode)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { OsuUid = "123", Platform = "qq", PlatformUid = "100", GameMode = 3 });
        await db.SaveChangesAsync();
        string? target = null;
        GameMode? receivedMode = null;
        var api = new RecordingOsuApiService { UserHandler = (id, gameMode) =>
        {
            target = id;
            receivedMode = gameMode;
            return new User { Id = 123, Username = "Player Name", CountryCode = "CN" };
        } };
        var controller = CreateController(db, api);
        var result = Assert.IsType<FileContentResult>(await controller.GetUserInfo(platform, uid, mode, username));
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(expectedTarget, target);
        Assert.Equal((GameMode)expectedMode, receivedMode);
    }

    [Theory]
    [InlineData(null, null, null, null, 400)]
    [InlineData(null, "qq", "missing", null, 404)]
    [InlineData("player", null, null, -1, 400)]
    [InlineData("player", null, null, 4, 400)]
    public async Task Invalid_identity_or_mode_does_not_call_osu(string? username, string? platform,
        string? uid, int? mode, int expectedStatus)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var api = new RecordingOsuApiService();
        var result = Assert.IsAssignableFrom<ObjectResult>(await CreateController(db, api).GetUserInfo(platform, uid, mode, username));
        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Missing_osu_player_returns_404_and_retryable_errors_propagate()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var api = new RecordingOsuApiService { UserHandler = (_, _) => throw new HttpRequestException("missing", null, HttpStatusCode.NotFound) };
        var controller = CreateController(db, api);
        var result = Assert.IsType<NotFoundObjectResult>(await controller.GetUserInfo(user_name: "missing"));
        Assert.Equal("osu! user not found", result.Value);
        api.UserHandler = (_, _) => throw new OsuQuotaExceededException();
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => controller.GetUserInfo(user_name: "player"));
    }

    private static AppDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static UserInfoController CreateController(AppDbContext db, IOsuApiService api)
    {
        var cache = new ImageCache();
        return new UserInfoController(db, Options.Create(new HistoryOptions()), api, cache,
            new DefaultUserInfoTheme(new Renderer(), cache, NullLogger<DefaultUserInfoTheme>.Instance),
            null!, null!, NullLogger<UserInfoController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    private sealed class Renderer : IRenderService
    {
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult(new byte[] { 137, 80, 78, 71 });
    }

    private sealed class ImageCache : IImageCacheService
    {
        public Task<byte[]> GetAvatarAsync(string avatarUrl, int userId) => Task.FromResult(new byte[] { 1 });
        public Task<byte[]?> GetUserBackgroundAsync(int userId) => Task.FromResult<byte[]?>(null);
        public Task<byte[]?> GetUserBannerAsync(string? bannerUrl, int userId) => Task.FromResult<byte[]?>(null);
        public Task SaveUserBackgroundAsync(int userId, byte[] data) => throw new NotSupportedException();
        public Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index) => throw new NotSupportedException();
    }
}
