using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Rendering.ScoreTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public class ScoreListControllerTests
{
    [Theory]
    [InlineData("recent", "default", true)]
    [InlineData("best", "default", true)]
    [InlineData("map", "default", true)]
    [InlineData("recent", "yaowan", false)]
    [InlineData("best", "yaowan", false)]
    [InlineData("map", "yaowan", false)]
    public async Task Single_score_endpoints_use_landscape_only_for_default_theme(string source, string themeName, bool landscape)
    {
        var recent = source == "recent";
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { Platform = "qq", PlatformUid = "1", OsuUserId = 42, GameMode = 0 });
        await db.SaveChangesAsync();
        var score = new Score
        {
            RulesetId = 0, Rank = Grade.A, Accuracy = .98, MaxCombo = 100, Pp = 200,
            Beatmap = new Beatmap { Id = 3881559, BeatmapsetId = 1885198, Version = "Elegy", Status = RankStatus.Ranked },
            Beatmapset = new BeatmapsetCompact { Id = 1885198, Title = "Epitaph" },
            Statistics = new Statistics { Great = 100, Miss = 1 }
        };
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, _) => new User { Id = 42, Username = "Molli", CountryCode = "CN" },
            BeatmapHandler = id => { Assert.Equal(3881559, id); return score.Beatmap; },
            BeatmapUserScoresHandler = (mapId, userId, mode) =>
            {
                Assert.Equal(3881559, mapId);
                Assert.Equal(42, userId);
                Assert.Equal(GameMode.Osu, mode);
                return [score];
            },
            ScoresHandler = (_, type, _, limit, offset, _, _) =>
            {
                Assert.Equal(recent ? ScoreType.Recent : ScoreType.Best, type);
                Assert.Equal(1, limit);
                Assert.Equal(2, offset);
                return [score];
            }
        };
        var renderer = new Capture();
        var theme = new DefaultScoreTheme(renderer, new AvatarCardImageCache([]), new PpCalculatorService(), NullLogger<DefaultScoreTheme>.Instance);
        var history = new HistoryService(db, api, null!, null!, null!, NullLogger<HistoryService>.Instance);
        var controller = new ScoreController(db, api, new SingleScoreFiles(), theme, history, NullLogger<ScoreController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = source == "map"
            ? await controller.UserScore("qq", "1", 3881559, theme: themeName)
            : recent
            ? await controller.RecentPlay("qq", "1", recent_index: 3, theme: themeName)
            : await controller.BestPlay("qq", "1", best_index: 3, theme: themeName);
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(result).ContentType);
        Assert.Equal(landscape, renderer.Html!.Contains("class=\"player-bar\""));
        Assert.Equal(landscape, renderer.Height == 0);
        Assert.Equal(themeName == "default", renderer.Html.Contains("Powered By MintAPI"));
        if (themeName == "default")
            Assert.Contains(source == "map" ? "MAP SCORE" : recent ? "RECENT PLAY" : "BEST PLAY", renderer.Html);
    }

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
        db.Users.Add(new UserModel { Platform = "qq", PlatformUid = "1", OsuUserId = 42, GameMode = 3 });
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
            : await controller.BestPlays("qq", "1", best_index: 3, legacy_only: false, best_end: 7);
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
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            Height = height;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }

    private sealed class SingleScoreFiles : IBeatmapFileService
    {
        public Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId)
            => Task.FromResult(Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"));
        public string GetBgFilename(string osuFilePath) => "";
        public Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null)
            => Task.FromResult(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII="));
    }
}
