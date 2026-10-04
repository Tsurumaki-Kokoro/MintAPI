using MintAPI.Rendering.AvatarCardTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class AvatarCardRenderingTests
{
    [Fact]
    public async Task Cards_render_square_with_background_and_complete_identity()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        // Deliberately non-square input verifies crop-to-fill rather than stretching.
        await using var sourceContext = await browser.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 400, Height = 200 } });
        var source = await sourceContext.NewPageAsync();
        await source.SetContentAsync("<body style='margin:0;background:#baa0df'><svg width='400' height='200'><circle cx='200' cy='100' r='65' fill='#65428c'/><circle cx='180' cy='85' r='8' fill='#ede7f6'/><circle cx='220' cy='85' r='8' fill='#ede7f6'/><path d='M175 120Q200 150 225 120' stroke='#ede7f6' stroke-width='8' fill='none'/></svg></body>");
        var avatar = await source.ScreenshotAsync();
        var renderer = new InspectingRenderer(browser);
        var theme = new AvatarCardTheme(new AvatarCardImageCache(avatar), renderer);
        var output = Path.Combine(Path.GetTempPath(), "mintapi-avatar-card-preview");
        Directory.CreateDirectory(output);
        foreach (var (name, country, filename) in new[]
        {
            ("peppy", "JP", "normal.png"),
            ("[WWWW-WWWW_WWW]", "US", "long-name.png"),
            ("WWWWWWWWWWWWWWW", "US", "wide-name.png"),
            ("玩家名", "XX", "missing-flag.png")
        })
        {
            var png = await theme.RenderAsync(new User { Id = 123, Username = name, CountryCode = country });
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, png[..4]);
            Assert.Equal(512, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(512, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
            await File.WriteAllBytesAsync(Path.Combine(output, filename), png);
        }
    }

    [Fact]
    public async Task Corrupt_avatar_is_rejected_by_browser()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var theme = new AvatarCardTheme(new AvatarCardImageCache([1, 2, 3]), new PlaywrightRenderer(browser));
        await Assert.ThrowsAsync<PlaywrightException>(() => theme.RenderAsync(new User { Id = 123, Username = "test" }));
    }

    private sealed class InspectingRenderer(PlaywrightBrowserProvider browser) : IRenderService
    {
        public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            var file = Path.Combine(Path.GetTempPath(), $"avatar_test_{Guid.NewGuid():N}.html");
            try
            {
                await File.WriteAllTextAsync(file, html, cancellationToken);
                await using var context = await browser.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height } });
                var page = await context.NewPageAsync();
                await page.GotoAsync(new Uri(file).AbsoluteUri);
                await page.EvaluateAsync("document.fonts.ready");
                Assert.Equal("rgb(237, 231, 246)", await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor"));
                Assert.Equal("400px", await page.Locator(".avatar").EvaluateAsync<string>("e => getComputedStyle(e).width"));
                Assert.Equal("cover", await page.Locator(".avatar").EvaluateAsync<string>("e => getComputedStyle(e).objectFit"));
                Assert.True(await page.Locator(".identity").EvaluateAsync<bool>("e => e.scrollWidth <= 480"));
                Assert.True(await page.Locator(".name").EvaluateAsync<bool>("e => { const r = e.getBoundingClientRect(); return r.left >= 16 && r.right <= 496 && r.bottom <= 512; }"));
                if (await page.Locator(".flag").CountAsync() > 0)
                    Assert.True(await page.Locator(".flag").EvaluateAsync<bool>("e => e.naturalWidth > 0"));
                return await new PlaywrightRenderer(browser).RenderHtmlAsync(html, width, height, cancellationToken);
            }
            finally { File.Delete(file); }
        }
    }
}
