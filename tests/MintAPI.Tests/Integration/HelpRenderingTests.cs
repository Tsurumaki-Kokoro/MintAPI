using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Rendering.HelpTheme;
using MintAPI.Services;

namespace MintAPI.Tests.Integration;

public sealed class HelpRenderingTests
{
    [Fact]
    public async Task Real_pipeline_renders_help_without_overflow_or_missing_assets()
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await provider.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(provider));
        var png = await new HelpTheme(capture).RenderAsync();
        Assert.True(png.Length > 1000);
        await using var context = await provider.Browser.NewContextAsync(new() { ViewportSize = new() { Width = HelpTheme.Width, Height = 1800 } });
        var page = await context.NewPageAsync();
        var previewHtml = Path.Combine(AppContext.BaseDirectory, "minthelp-inspection.html");
        await File.WriteAllTextAsync(previewHtml, capture.Html);
        await page.GotoAsync(new Uri(previewHtml).AbsoluteUri);
        await page.EvaluateAsync("document.fonts.ready");
        Assert.True(await page.EvaluateAsync<bool>("document.fonts.check('24px Harmony')"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => [...document.images].filter(i => !i.complete || i.naturalWidth === 0).length"));
        Assert.Equal(3, await page.Locator(".grid > .column").CountAsync());
        Assert.Equal("grid", await page.Locator(".grid").EvaluateAsync<string>("e => getComputedStyle(e).display"));
        Assert.Empty(await page.EvaluateAsync<string[]>("""() => [...document.querySelectorAll('.grid *, footer')].filter(e => e.scrollWidth > e.clientWidth + 2).map(e => e.textContent)"""));
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('footer').getBoundingClientRect().bottom < document.querySelector('.mint-attribution').getBoundingClientRect().top"));
        if (Environment.GetEnvironmentVariable("MINTHELP_PREVIEW_DIR") is not string output) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, "minthelp.png"), png);
        await File.WriteAllTextAsync(Path.Combine(output, "minthelp.html"), capture.Html);
        await page.EvaluateAsync("document.body.style.zoom = '0.257142857'");
        await page.SetViewportSizeAsync(720, 600);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "minthelp-chat.png"), Clip = new() { X = 0, Y = 0, Width = 720, Height = await page.Locator("body").EvaluateAsync<float>("e => e.getBoundingClientRect().height") } });
    }

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }
}
