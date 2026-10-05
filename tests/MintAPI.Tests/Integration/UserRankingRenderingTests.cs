using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintAPI.Controllers;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class UserRankingRenderingTests
{
    [Fact]
    public async Task Rankings_render_png_without_horizontal_overflow()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var avatar = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "osu-web", "public", "images", "layout", "avatar-guest@2x.png"));
        var renderer = new UserRankingRenderer(capture, new AvatarCardImageCache(avatar));
        var countries = new[] { "CN", "JP", "US", "KR", "GB", "DE", "CA" };
        var names = new[] { "月白 / Tsukishiro", "Aster", "星野-Hoshino", "Lumen", "Very_Long_Player_Name_With_Extra_Text", "Mint", "未排名玩家" };
        var users = names.Select((name, i) => new UserRankingEntry((10001 + i).ToString(), (20001 + i).ToString(),
            name, i == 6 ? null : 128 + i * 1604, i == 6 ? null : 12480.35 - i * 1200.4, i == 6 ? null : 99.42 - i * .32, "https://a.ppy.sh/" + (20001 + i), countries[i])).ToList();
        var modes = Enumerable.Range(0, 4).Select(mode => new UserModeRanking(mode,
            users.Skip(mode).Concat(users.Take(mode)).Take(5).Select((u, i) => u with { Rank = 50 + mode * 100 + i * 240, Pp = 13200 - mode * 2000 - i * 400 }).ToList())).ToList();
        foreach (var topFive in new[] { false, true })
        {
            var png = await renderer.RenderAsync("qq", topFive ? modes : [new UserModeRanking(0, users)], topFive, default);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8));
            await using var context = await browser.Browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(capture.Width, 1000);
            await page.SetContentAsync(capture.Html);
            Assert.Equal(capture.Width, await page.EvaluateAsync<int>("document.documentElement.scrollWidth"));
            Assert.Equal(topFive ? 4 : 1, await page.Locator("section").CountAsync());
            Assert.Equal(topFive ? 20 : 7, await page.Locator("tbody tr").CountAsync());
            Assert.Equal(topFive ? 20 : 7, await page.Locator("img.avatar").CountAsync());
            Assert.Equal(topFive ? 20 : 7, await page.Locator("img.flag").CountAsync());
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("imgs => imgs.every(i => i.complete && i.naturalWidth > 0)"));
            Assert.True(await page.Locator(".player").EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth)"));
            var output = Environment.GetEnvironmentVariable("RANKING_PREVIEW_DIR");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(Path.Combine(output, topFive ? "ranking-top5.png" : "ranking-single.png"), png);
            }
        }
        await renderer.RenderAsync("qq", [new UserModeRanking(0, [users[0] with { AvatarUrl = "", CountryCode = "../invalid" }])], false, default);
        Assert.Contains("avatar fallback", capture.Html);
        Assert.DoesNotContain("class='flag'", capture.Html);
        Assert.DoesNotContain("<img", capture.Html);
        var escaped = await renderer.RenderAsync("<qq>", [new UserModeRanking(0, [users[0] with { Username = "<script>alert(1)</script>" }])], false, default);
        Assert.NotEmpty(escaped);
        Assert.Contains("&lt;script&gt;", capture.Html);
        Assert.DoesNotContain("<script>", capture.Html);
        await renderer.RenderAsync("qq", [new UserModeRanking(0, users.Concat(Enumerable.Repeat(users[0], 93)).ToList())], false, default);
        Assert.Equal(100, capture.Html.Split("<tr class=").Length - 1);
    }

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Width { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Width = width;
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }
}
