using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Rendering.ScoreTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public class NewBestPlaysControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetches_two_BP_pages_and_renders_original_rank_or_returns_404(bool hasNewBp)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { Platform = "qq", PlatformUid = "1", OsuUid = "42", GameMode = 3 });
        await db.SaveChangesAsync();
        var offsets = new List<int?>();
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, mode) =>
            {
                Assert.Equal(GameMode.Mania, mode);
                return new User { Id = 42, Username = "Aster <script>", CountryCode = "CN" };
            },
            ScoresHandler = (id, type, mode, limit, offset, fails, legacy) =>
            {
                Assert.Equal(42, id); Assert.Equal(ScoreType.Best, type);
                Assert.Equal(GameMode.Mania, mode); Assert.Equal(100, limit); Assert.False(legacy);
                offsets.Add(offset);
                return Enumerable.Range(0, offset == 0 ? 100 : 1).Select(_ => new Score
                {
                    EndedAt = offset == 100 && hasNewBp ? DateTimeOffset.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow.AddDays(-10),
                    RulesetId = 3, Rank = Grade.A, Pp = 123.45, Accuracy = .9876,
                    Beatmap = new Beatmap { Version = "Another" },
                    Beatmapset = new BeatmapsetCompact { Title = "星の世界 / A song for a long journey", Artist = "Lumen & 月白" }
                }).ToList();
            }
        };
        var capture = new Capture();
        var theme = new DefaultScoreTheme(capture, new AvatarCardImageCache([]), null!, NullLogger<DefaultScoreTheme>.Instance);
        var controller = new ScoreController(db, api, null!, theme, null!, NullLogger<ScoreController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = await controller.NewBestPlays("qq", "1", legacy_only: false);
        Assert.Equal(new int?[] { 0, 100 }, offsets);
        if (!hasNewBp) { Assert.IsType<NotFoundObjectResult>(result); return; }
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(result).ContentType);
        Assert.Contains("#101", capture.Html);
        Assert.Contains("NEW BEST PLAYS", capture.Html);
        Assert.Contains("近 1 日 · 1 项", capture.Html);
        Assert.Contains("UTC", capture.Html);
        Assert.Contains("&lt;script&gt;", capture.Html);
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1500, capture.Height);
        await page.SetContentAsync(capture.Html);
        Assert.Equal($"1500x{capture.Height}", await page.EvaluateAsync<string>("`${document.documentElement.scrollWidth}x${document.documentElement.scrollHeight}`"));
        Assert.True(await page.Locator(".results").EvaluateAllAsync<bool>("els => els.every(e => e.scrollHeight <= e.closest('.row').clientHeight)"));
        var output = Environment.GetEnvironmentVariable("SCORE_PREVIEW_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(output, "new-best-plays.png"), FullPage = true });
        }
    }

    private sealed class Capture : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Height = height;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }
}
