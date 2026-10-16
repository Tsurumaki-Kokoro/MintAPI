using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MintAPI.Errors;
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
    [InlineData(null, 3)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task Performance_analysis_uses_requested_or_bound_mode_without_changing_binding(int? requestedMode, int expectedMode)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var binding = new UserModel { OsuUid = "123", Platform = "qq", PlatformUid = "100", GameMode = 3 };
        db.Users.Add(binding);
        await db.SaveChangesAsync();
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, mode) =>
            {
                Assert.Equal((GameMode)expectedMode, mode);
                return new User { Id = 123 };
            },
            ScoresHandler = (_, _, mode, _, _, _, _) =>
            {
                Assert.Equal((GameMode)expectedMode, mode);
                return [];
            }
        };
        Assert.IsType<NotFoundObjectResult>(await CreateController(db, api).PerformanceAnalyze("qq", "100", game_mode: requestedMode));
        Assert.Equal(2, api.CallCount);
        Assert.Equal(3, binding.GameMode);
        Assert.Equal(EntityState.Unchanged, db.Entry(binding).State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task Performance_analysis_rejects_invalid_mode_before_calling_osu(int mode)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var api = new RecordingOsuApiService();
        Assert.IsType<BadRequestObjectResult>(await CreateController(db, api).PerformanceAnalyze("qq", "100", game_mode: mode));
        Assert.Equal(0, api.CallCount);
    }

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
        Assert.Equal("OSU_USER_NOT_FOUND", Assert.IsType<MintAPI.Errors.ApiError>(result.Value).Code);
        api.UserHandler = (_, _) => throw new OsuQuotaExceededException();
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => controller.GetUserInfo(user_name: "player"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(7, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(int.MaxValue, false)]
    public async Task Comparison_defaults_to_today_without_falling_back_to_other_days(int? days, bool hasToday)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        var today = new HistoryOptions().Today(DateTimeOffset.UtcNow);
        db.UserOsuInfoHistories.Add(new UserOsuInfoHistory
        {
            OsuUid = "123", GameMode = 0, Date = today.AddDays(-7), Pp = 100
        });
        if (hasToday)
            db.UserOsuInfoHistories.Add(new UserOsuInfoHistory
            {
                OsuUid = "123", GameMode = 0, Date = today, Pp = 200
            });
        await db.SaveChangesAsync();
        var renderer = new Renderer();
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, _) => new User
            {
                Id = 123, Username = "player", CountryCode = "CN",
                Statistics = new UserStatistics { Pp = 250 }
            }
        };
        Assert.IsType<FileContentResult>(await CreateController(db, api, renderer)
            .GetUserInfo(user_name: "player", compare_with: days));
        if (days > 0)
        {
            Assert.Contains("数据对比于 7 天前", renderer.Html);
            Assert.Contains("↑150", renderer.Html);
        }
        else if (hasToday)
            Assert.Contains("数据对比于当日凌晨", renderer.Html);
        else
            Assert.DoesNotContain("数据对比于", renderer.Html);
    }

    [Theory]
    [InlineData("-1", false, "INVALID_ARGUMENT", 400)]
    [InlineData("2147483648", false, "INVALID_ARGUMENT", 400)]
    [InlineData("7", true, "OSU_API_UNAVAILABLE", 502)]
    public async Task Comparison_errors_keep_image_accept_http_contract(string days, bool upstreamFails, string code, int status)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, _) => throw new HttpRequestException("private-upstream-failure")
        };
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(UserInfoController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient<UserInfoController>(_ => CreateController(db, api));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/user_info?user_name=player&compare_with={days}");
            request.Headers.Accept.ParseAdd("image/png");
            using var response = await http.SendAsync(request);
            await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-upstream-failure");
            Assert.Equal(upstreamFails ? 1 : 0, api.CallCount);
        }
        finally { await app.StopAsync(); }
    }

    private static AppDbContext CreateDb(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static UserInfoController CreateController(AppDbContext db, IOsuApiService api, Renderer? renderer = null)
    {
        var cache = new ImageCache();
        return new UserInfoController(db, Options.Create(new HistoryOptions()), api, cache,
            new DefaultUserInfoTheme(renderer ?? new Renderer(), cache, NullLogger<DefaultUserInfoTheme>.Instance),
            null!, null!, NullLogger<UserInfoController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    private sealed class Renderer : IRenderService
    {
        public string Html { get; private set; } = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
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
