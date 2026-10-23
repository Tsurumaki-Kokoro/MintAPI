using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Models.Entities;
using MintAPI.Rendering.PlayerFeatures;
using MintAPI.Services;
using MintAPI.Services.PlayerFeatures;

namespace MintAPI.Tests.Integration;

public sealed class PlayerFeatureRenderingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Real_pipeline_renders_both_reports_with_mode_specific_statistics_and_long_escaped_text(int mode)
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await provider.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(provider));
        var renderer = new PlayerFeatureRenderer(capture, new PlayerFeatureTests.Images(), new PlayerFeatureTests.Covers(), NullLogger<PlayerFeatureRenderer>.Instance);
        var user = PlayerFeatureTests.Fixture.User();
        var second = PlayerFeatureTests.Fixture.User(43);
        if (mode == 3) { user.Username = "一位有着很长名字的玩家_LongMultilingualName_123456789"; second.Username = "AnotherVeryLongPlayerName_月の彼方_987654321"; }
        var score = PlayerFeatureTests.Fixture.Score(mode: mode);
        var other = PlayerFeatureTests.Fixture.Score(2, mode);
        other.Statistics = null; other.Pp = null;
        score.Beatmap!.Version = mode == 3 ? "月明かりの下で / An exceptionally long difficulty name <unsafe> & rhythm" : "Four Dimensions";
        var comparison = new ScoreComparison(score.Beatmap, mode, new(user, score, "official"), new(second, other, "local"), true);
        await SaveAsync(await renderer.ComparisonAsync(comparison, default), capture.Html, $"comparison-{mode}");
        Assert.Contains("&lt;test&gt;", capture.Html); Assert.DoesNotContain("player-covers", capture.Html);
        var start = new DateOnly(2026, 10, 12);
        var rows = Enumerable.Range(0, 8).Where(i => mode != 3 || i != 3).Select(i => new UserOsuInfoHistory
        {
            Date = start.AddDays(i), Pp = 6292.12 + i * 18.35, GlobalRank = 25102 - i * 183,
            PlayCount = 1000 + i * 26, PlayTime = 36000 + i * 3400
        }).ToList();
        var report = new WeeklyReport(user, mode, start, start.AddDays(7), rows[0], mode == 3 ? null : rows[^1], rows,
            mode is 2 or 3 ? [] : [new(8, score), new(12, other), new(18, score)], mode != 3, null, "Asia/Shanghai");
        await SaveAsync(await renderer.WeeklyAsync(report, default), capture.Html, $"weekly-{mode}");
        Assert.Contains("本周新增 BP", capture.Html); Assert.Contains("border-radius:12px", capture.Html);
        Assert.Contains("Powered By MintAPI", capture.Html);

        async Task SaveAsync(byte[] png, string html, string name)
        {
            Assert.True(png.Length > 1000);
            await using var context = await provider.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1200, Height = 900 } });
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html); await page.EvaluateAsync("document.fonts.ready");
            var overflows = await page.EvaluateAsync<string[]>("""() => [...document.querySelectorAll('main *')].filter(e=>e.getBoundingClientRect().right>1201 || e.scrollWidth>e.clientWidth+2).map(e=>e.tagName+': '+e.textContent.slice(0,70))""");
            Assert.Empty(overflows);
            Assert.Equal(0, await page.EvaluateAsync<int>("() => [...document.images].filter(i=>!i.complete || i.naturalWidth===0).length"));
            if (Environment.GetEnvironmentVariable("PLAYER_FEATURE_PREVIEW_DIR") is not string root) return;
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(Path.Combine(root, name + ".png"), png);
            await File.WriteAllTextAsync(Path.Combine(root, name + ".html"), html);
            // Inspect at a representative chat display width without changing the export layout.
            await page.SetViewportSizeAsync(600, 900);
            await page.EvaluateAsync("document.body.style.zoom='0.5'");
            var height = await page.Locator("body").EvaluateAsync<float>("e=>e.getBoundingClientRect().height");
            await page.ScreenshotAsync(new() { Path = Path.Combine(root, name + "-chat.png"), Clip = new() { X = 0, Y = 0, Width = 600, Height = height } });
        }
    }

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        { Html = html; return inner.RenderHtmlAsync(html, width, height, cancellationToken); }
    }
}
