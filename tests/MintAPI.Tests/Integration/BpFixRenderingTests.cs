using MintAPI.Rendering.ScoreTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public class BpFixRenderingTests
{
    [Fact]
    public async Task Fix_report_renders_comparisons_without_overflow()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture();
        var theme = new DefaultScoreTheme(capture, new AvatarCardImageCache([]), null!, NullLogger<DefaultScoreTheme>.Instance);
        var user = new User { Username = "Aster", CountryCode = "CN", Statistics = new UserStatistics { Pp = 5432.1 } };
        var scores = Enumerable.Range(0, 3).Select(i => new Score
        {
            Passed = true, Pp = 300 - i * 20, Accuracy = .9876, Rank = Grade.A, MaxCombo = 1200,
            Beatmap = new Beatmap { Id = 100 + i, Version = "Another [Long Difficulty Name]" },
            Beatmapset = new BeatmapsetCompact { Title = "星の世界 / A song for a long journey", Artist = "Lumen & 月白" },
            Statistics = new Statistics { Miss = 2 },
            Mods = [new NonLegacyMod { Acronym = "HD" }, new NonLegacyMod { Acronym = "NC" }]
        }).ToList();
        var report = BpFixService.BuildReport(user, scores, Enumerable.Range(0, 3)
            .ToDictionary(i => i, i => new PpResult(350 + i * 20, 6, 1500)), 3);
        await theme.RenderFixAsync(report, user);
        Assert.Contains("BP FIX", capture.Html);
        Assert.Contains("保持原准确率", capture.Html);
        Assert.Contains("1,200x → 1,500x", capture.Html);
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1500, capture.Height);
        await page.SetContentAsync(capture.Html);
        Assert.Equal($"1500x{capture.Height}", await page.EvaluateAsync<string>("`${document.documentElement.scrollWidth}x${document.documentElement.scrollHeight}`"));
        Assert.InRange(await page.Locator("footer").EvaluateAsync<double>("e => e.getBoundingClientRect().bottom"), capture.Height - 25, capture.Height - 24);
        Assert.True(await page.Locator(".results").EvaluateAllAsync<bool>("els => els.every(e => e.scrollHeight <= e.closest('.row').clientHeight)"));
        var output = Environment.GetEnvironmentVariable("SCORE_PREVIEW_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(output, "bp-fix.png"), FullPage = true });
        }
    }

    private sealed class Capture : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Height = height;
            return Task.FromResult(Array.Empty<byte>());
        }
    }
}
