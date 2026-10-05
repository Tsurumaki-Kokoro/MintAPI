using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class HistoryAttributionRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_images_show_attribution_inside_the_canvas(bool scores)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture();
        var renderer = new HistoryRenderer(capture);
        if (scores)
            await renderer.RenderScoresAsync("成绩历史", "本地采集历史", [new Score { Accuracy = .9876, MaxCombo = 1000, Pp = 250, EndedAt = DateTimeOffset.UtcNow }], 0, 1, default);
        else
            await renderer.RenderTrendAsync("PP / Rank 历史", [new HistoryPoint(new DateOnly(2026, 10, 5), 5000, 10000, "local")], default);
        await using var context = await browser.Browser.NewContextAsync(new() { ViewportSize = new() { Width = capture.Width, Height = capture.Height } });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(capture.Html);
        await RenderingAttributionAssertions.CheckAsync(page);
        Assert.True(await page.Locator("section").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('.mint-attribution').getBoundingClientRect().top)"));
    }

    private sealed class Capture : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Width = width; Height = height;
            return Task.FromResult(Array.Empty<byte>());
        }
    }
}
