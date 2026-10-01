using HitCircleAPI.Controllers;
using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Rendering.ScoreTheme;
using HitCircleAPI.Services;
using HitCircleAPI.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Tests.Integration;

public class ScoreListControllerTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    public async Task List_ranges_forward_filters_and_display_actual_returned_range(bool recent, int available)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { Platform = "qq", PlatformUid = "1", OsuUid = "42", GameMode = 3 });
        await db.SaveChangesAsync();
        var api = new RecordingOsuApiService
        {
            UserHandler = (id, mode) =>
            {
                Assert.Equal("42", id);
                Assert.Equal(GameMode.Mania, mode);
                return new User { Id = 42, Username = "Molli", CountryCode = "CN" };
            },
            ScoresHandler = (id, type, mode, limit, offset, fails, legacy) =>
            {
                Assert.Equal(42, id);
                Assert.Equal(recent ? ScoreType.Recent : ScoreType.Best, type);
                Assert.Equal(GameMode.Mania, mode);
                Assert.Equal(5, limit);
                Assert.Equal(2, offset);
                Assert.Equal(recent ? true : (bool?)null, fails);
                Assert.False(legacy);
                return Enumerable.Range(0, available).Select(_ => new Score
                {
                    RulesetId = 3, Rank = Grade.A, Passed = true, Pp = 200,
                    EndedAt = DateTimeOffset.Parse("2026-09-30T08:00:00Z")
                }).ToList();
            }
        };
        var renderer = new Capture();
        var theme = new DefaultScoreTheme(renderer, new AvatarCardImageCache([]), null!, NullLogger<DefaultScoreTheme>.Instance);
        var controller = new ScoreController(db, api, null!, theme, null!, NullLogger<ScoreController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = recent
            ? await controller.RecentPlay("qq", "1", include_fails: true, recent_index: 3, legacy_only: false, recent_end: 7)
            : await controller.BestPlay("qq", "1", best_index: 3, legacy_only: false, best_end: 7);
        if (available == 0)
        {
            Assert.IsType<NotFoundObjectResult>(result);
            Assert.Null(renderer.Html);
        }
        else
        {
            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("image/png", file.ContentType);
            Assert.Contains("#3–4 · 2 PLAYS", renderer.Html);
            Assert.Contains(recent ? "RECENT PLAYS" : "BEST PLAYS", renderer.Html);
            Assert.Contains("osu!mania", renderer.Html);
        }
        Assert.Equal(2, api.CallCount);
    }

    private sealed class Capture : IRenderService
    {
        public string? Html { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }
}
