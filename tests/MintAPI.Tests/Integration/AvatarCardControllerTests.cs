using System.Net;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Rendering.AvatarCardTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class AvatarCardControllerTests
{
    [Theory]
    [InlineData("123", null, null)]
    [InlineData("peppy", null, null)]
    [InlineData(" peppy ", "qq", "missing")]
    [InlineData(null, "qq", "1")]
    public async Task Query_resolves_direct_player_or_binding(string? user, string? platform, string? platformUid)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { OsuUid = "123", Platform = "qq", PlatformUid = "1" });
        await db.SaveChangesAsync();
        string? target = null;
        var api = new RecordingOsuApiService { UserHandler = (id, _) =>
        {
            target = id;
            return new User { Id = 123, Username = "<peppy>", CountryCode = "XX" };
        } };
        var renderer = new AvatarCardRecordingRenderer();
        var controller = CreateController(db, api, [1], renderer);
        var result = Assert.IsType<FileContentResult>(await controller.GetAvatarCard(user, platform, platformUid));
        Assert.Equal(user?.Trim() ?? "123", target);
        Assert.Equal("image/png", result.ContentType);
        Assert.Contains("&lt;peppy&gt;", renderer.Html);
        Assert.Contains("class='country'>XX", renderer.Html);
        Assert.DoesNotContain("<peppy>", renderer.Html);
    }

    [Theory]
    [InlineData(null, null, null, 400)]
    [InlineData(" ", "qq", null, 400)]
    [InlineData(null, "qq", "missing", 404)]
    public async Task Missing_identity_or_binding_returns_expected_status(string? user, string? platform, string? uid, int status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var api = new RecordingOsuApiService();
        var controller = CreateController(db, api, [1], new AvatarCardRecordingRenderer());
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.GetAvatarCard(user, platform, uid));
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(0, api.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.BadGateway, 500)]
    public async Task External_failure_preserves_not_found(HttpStatusCode externalStatus, int expected)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var api = new RecordingOsuApiService { UserHandler = (_, _) => throw new HttpRequestException("error", null, externalStatus) };
        var result = Assert.IsAssignableFrom<ObjectResult>(await CreateController(db, api, [1], new AvatarCardRecordingRenderer()).GetAvatarCard("123"));
        Assert.Equal(expected, result.StatusCode);
    }

    [Fact]
    public async Task Missing_avatar_returns_error_and_quota_exceptions_propagate()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var api = new RecordingOsuApiService { UserHandler = (_, _) => new User { Id = 123 } };
        var renderer = new AvatarCardRecordingRenderer();
        var controller = CreateController(db, api, [], renderer);
        var result = Assert.IsType<ObjectResult>(await controller.GetAvatarCard("123"));
        Assert.Equal(500, result.StatusCode);
        Assert.Null(renderer.Html);
        api.UserHandler = (_, _) => throw new OsuQuotaExceededException();
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => controller.GetAvatarCard("123"));
    }

    private static AvatarCardController CreateController(AppDbContext db, IOsuApiService api, byte[] avatar, IRenderService renderer) =>
        new(db, api, new AvatarCardTheme(new AvatarCardImageCache(avatar), renderer), NullLogger<AvatarCardController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
}
