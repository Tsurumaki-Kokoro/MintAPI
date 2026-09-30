using System.Text;
using Microsoft.Playwright;

namespace HitCircleAPI.Services;

/// <summary>使用 Chromium 将 HTML 渲染为 PNG。</summary>
public sealed class PlaywrightRenderer(IBrowserProvider browserProvider) : IRenderService
{
    public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var browser = browserProvider.Browser;
        var tmpFile = Path.Combine(Path.GetTempPath(), $"hcapi_{Guid.NewGuid():N}.html");

        try
        {
            await File.WriteAllTextAsync(tmpFile, html, Encoding.UTF8, cancellationToken);

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
            });
            using var cancellationRegistration = cancellationToken.Register(
                static state => _ = CloseContextOnCancellationAsync((IBrowserContext)state!), context);
            cancellationToken.ThrowIfCancellationRequested();
            var page = await context.NewPageAsync();

            // 等待图片和字体加载。
            await page.GotoAsync($"file://{tmpFile}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
            });
            await page.EvaluateAsync("document.fonts.ready");

            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Type = ScreenshotType.Png,
                Clip = new Clip { X = 0, Y = 0, Width = width, Height = height },
            });
        }
        finally
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
        }
    }

    private static async Task CloseContextOnCancellationAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (Exception)
        {
            // The render may already have closed or disposed the context.
        }
    }
}
