using MintAPI.Models.Entities;
using MintAPI.Rendering.UserInfoTheme;
using MintAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

[Trait("Category", "Integration")]
public class UserInfoRenderingTests
{
    [Theory]
    [InlineData("OSU", true, 4)]
    [InlineData("TAIKO", false, 0)]
    [InlineData("FRUITS", true, 30)]
    [InlineData("MANIA", false, 4)]
    public async Task Banner_and_grouped_data_render_with_readable_values_and_complete_badges(string mode, bool hasBanner, int badgeCount)
    {
        var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");
        var bannerPath = Environment.GetEnvironmentVariable("USER_INFO_PREVIEW_BANNER");
        var avatarPath = Environment.GetEnvironmentVariable("USER_INFO_PREVIEW_AVATAR");
        var images = new Images(avatarPath is null ? pixel : await File.ReadAllBytesAsync(avatarPath),
            hasBanner ? bannerPath is null ? pixel : await File.ReadAllBytesAsync(bannerPath) : null, pixel);
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var renderer = new Capture(new PlaywrightRenderer(browser));
        var theme = new DefaultUserInfoTheme(renderer, images, NullLogger<DefaultUserInfoTheme>.Instance);
        var user = new User
        {
            Id = 42, Username = "Aster <script>alert(1)</script> & 玩家", CountryCode = "CN", IsSupporter = true,
            Cover = new Cover { Url = "https://assets.ppy.sh/user-profile-covers/42/banner.jpg" },
            Badges = Enumerable.Range(0, badgeCount).Select(i => new UserBadge { ImageUrl = "badge", Description = "Badge <script> & \" " + i }).ToList(),
            Statistics = new UserStatistics
            {
                Pp = 12345, GlobalRank = 1234, CountryRank = 68, Level = new UserLevel { Current = 102, Progress = 73 },
                HitAccuracy = 98.62, PlayCount = 78542, PlayTime = 1_553_467,
                RankedScore = 21_376_852_491, TotalScore = 98_763_254_810, TotalHits = 14_786_520,
                GradeCounts = new UserGradeCounts { Ssh = 147, Ss = 82, Sh = 1246, S = 2387, A = 1654 }
            }
        };
        var history = new UserOsuInfoHistory
        {
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(-7)), GlobalRank = 1286, CountryRank = 70,
            Pp = 12313, Accuracy = 98.59, PlayCount = 78478, TotalHits = 14777099
        };
        var png = await theme.RenderAsync(user, history, mode);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.Equal(user.Cover.Url, images.RequestedBannerUrl);
        Assert.Equal(hasBanner ? 0 : 1, images.LocalRequests);
        Assert.DoesNotContain("<script>", renderer.Html);
        Assert.Contains("&lt;script&gt;", renderer.Html);
        Assert.Contains("21,376,852,491", renderer.Html);
        Assert.Contains("98,763,254,810", renderer.Html);
        Assert.Contains("+9,421", renderer.Html);
        Assert.Contains("+0.03%", renderer.Html);
        Assert.Contains("17d 23h 31m 7s", renderer.Html);
        Assert.DoesNotContain("gradient(", renderer.Html);

        await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1000, Height = renderer.Height } });
        var page = await context.NewPageAsync();
        var path = Path.Combine(Path.GetTempPath(), "userinfo-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            await File.WriteAllTextAsync(path, renderer.Html);
            await page.GotoAsync(new Uri(path).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            Assert.Equal($"1000x{renderer.Height}", await page.EvaluateAsync<string>("document.documentElement.scrollWidth + 'x' + document.documentElement.scrollHeight"));
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
            Assert.Equal(badgeCount, await page.Locator(".badges img").CountAsync());
            if (badgeCount <= 9)
                Assert.Equal(280, await page.Locator(".profile").EvaluateAsync<int>("e => e.clientHeight"));
            Assert.DoesNotContain("玩家徽章", renderer.Html);
            Assert.DoesNotContain("暂无徽章", renderer.Html);
            Assert.True(await page.Locator(".profile .badges img").EvaluateAllAsync<bool>("els => els.every(e => { const b=e.getBoundingClientRect(), p=e.closest('.profile').getBoundingClientRect(), i=document.querySelector('.identity').getBoundingClientRect(); return b.top>=i.bottom && b.bottom<=p.bottom && b.left>=p.left && b.right<=p.right; })"));
            Assert.True(await page.Locator(".grades").EvaluateAsync<bool>("e => e.getBoundingClientRect().bottom < document.querySelector('footer').getBoundingClientRect().top"));
            Assert.True(await page.Locator(".metric-value, .activity-value, .secondary-item").EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth + 1)"));
            Assert.Equal("52px", await page.Locator(".username").EvaluateAsync<string>("e => getComputedStyle(e).fontSize"));
            Assert.Equal("none", await page.Locator(".profile-cover").EvaluateAsync<string>("e => getComputedStyle(e).filter"));
        }
        finally { File.Delete(path); }

        var directory = Environment.GetEnvironmentVariable("USER_INFO_PREVIEW_DIR");
        if (directory is not null && mode == "OSU")
        {
            Directory.CreateDirectory(directory);
            user.Username = "Aster";
            png = await theme.RenderAsync(user, history, mode);
            await File.WriteAllBytesAsync(Path.Combine(directory, "user-info.png"), png);
            await File.WriteAllTextAsync(Path.Combine(directory, "user-info.html"), renderer.Html);
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("yaowan")]
    public async Task Missing_stats_banner_and_history_still_render(string themeName)
    {
        var images = new Images([], null, null);
        var renderer = new Capture(null);
        var theme = new DefaultUserInfoTheme(renderer, images, NullLogger<DefaultUserInfoTheme>.Instance);
        await theme.RenderAsync(new User { Username = "New player", CountryCode = "XX" }, null, "OSU", themeName);
        Assert.Contains("New player", renderer.Html);
        Assert.DoesNotContain("<img class=\"profile-cover\"", renderer.Html);
        Assert.Equal(1, images.LocalRequests);
        Assert.Null(images.RequestedBannerUrl);
        Assert.Equal(themeName == "default" ? 1220 : 1350, renderer.Height);
    }

    private sealed class Images(byte[] avatar, byte[]? banner, byte[]? background) : IImageCacheService
    {
        public string? RequestedBannerUrl { get; private set; }
        public int LocalRequests { get; private set; }
        public Task<byte[]> GetAvatarAsync(string avatarUrl, int userId) => Task.FromResult(avatar);
        public Task<byte[]?> GetUserBannerAsync(string? bannerUrl, int userId) { RequestedBannerUrl = bannerUrl; return Task.FromResult(banner); }
        public Task<byte[]?> GetUserBackgroundAsync(int userId) { LocalRequests++; return Task.FromResult(background); }
        public Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index) => Task.FromResult(avatar);
        public Task SaveUserBackgroundAsync(int userId, byte[] data) => throw new NotSupportedException();
    }
    private sealed class Capture(IRenderService? inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Height = height; Assert.Equal(1000, width);
            return inner?.RenderHtmlAsync(html, width, height, cancellationToken) ?? Task.FromResult(Array.Empty<byte>());
        }
    }
}
