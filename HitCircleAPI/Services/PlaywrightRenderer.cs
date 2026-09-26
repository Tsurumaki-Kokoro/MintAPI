using System.Text;
using Microsoft.Playwright;

namespace HitCircleAPI.Services;

/// <summary>
/// 用 Chromium 把 HTML 截成 PNG。并发上限与超时由 <see cref="GuardedRenderService"/> 负责，
/// 这里只管"怎么渲染"。
/// </summary>
public sealed class PlaywrightRenderer(IBrowserProvider browserProvider) : IRenderService
{
    public async Task<byte[]> RenderHtmlAsync(string html, int width, int height)
    {
        var browser = browserProvider.Browser;
        var tmpFile = Path.Combine(Path.GetTempPath(), $"hcapi_{Guid.NewGuid():N}.html");

        try
        {
            await File.WriteAllTextAsync(tmpFile, html, Encoding.UTF8);

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
            });
            var page = await context.NewPageAsync();

            // Load 覆盖 <img>；@font-face 不在 load 事件里，必须单独等 fonts.ready。
            // 不用 NetworkIdle：它有 500ms 静默窗口的硬底，页面挂住时会一直等到默认超时。
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
}
