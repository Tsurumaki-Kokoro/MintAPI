using MintAPI.Rendering.MultiplayerTheme;
using MintAPI.Services;
using MintAPI.Tests.Unit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace MintAPI.Tests.Integration;

[Trait("Category", "Integration")]
public class MultiplayerRenderingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Templates_render_without_clipping_and_escape_external_text(bool team)
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await provider.StartAsync();
        var capture = new CaptureRenderer(new PlaywrightRenderer(provider));
        var theme = new MultiplayerTheme(capture, new EmptyImageCache(), NullLogger<MultiplayerTheme>.Instance);
        var match = MultiplayerDataTests.Sample(team, 3);
        match.Users[0].Username = "Aster <script>alert(1)</script> & Friends";
        foreach (var item in match.EventList)
        {
            item.Game!.Scores[0].Score = 912345;
            item.Game.Scores[1].Score = 723456;
        }
        var names = new[] { "月白", "Lumen", "starlight", "Kaze", "Celeste", "North" };
        for (var i = 0; i < names.Length; i++)
        {
            var id = i + 3;
            match.Users.Add(new MintOsuApi.Models.UserCompact { Id = id, Username = names[i], AvatarUrl = $"https://a.ppy.sh/{id}" });
            foreach (var item in match.EventList)
                item.Game!.Scores.Add(new MintOsuApi.Models.LegacyScore
                {
                    UserId = id, Score = 870000 - i * 87000 + item.Id * 4000, Accuracy = .992 - i * .014,
                    MaxCombo = 1024 - i * 65, Mods = i % 2 == 0 ? MintOsuApi.Mods.Mod.HD : MintOsuApi.Mods.Mod.NM,
                    Match = new MintOsuApi.Models.ScoreMatchInfo { Team = team ? (i < 3 ? "red" : "blue") : "none", Pass = true }
                });
        }
        var data = MultiplayerData.Build(match);
        var history = await theme.RenderHistoryAsync(data, 1);
        await CheckLayoutAsync(provider, capture);
        Assert.Contains("&lt;script&gt;", capture.Html);
        var directory = Environment.GetEnvironmentVariable("MULTIPLAYER_PREVIEW_DIR");
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            match.Users[0].Username = "Aster";
            data = MultiplayerData.Build(match);
            history = await theme.RenderHistoryAsync(data, 1);
            await File.WriteAllBytesAsync(Path.Combine(directory, team ? "history-team.png" : "history-individual.png"), history);
        }
        var rating = await theme.RenderRatingAsync(data, "osuplus", 1);
        await CheckLayoutAsync(provider, capture);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, rating[..4]);
        if (directory != null)
            await File.WriteAllBytesAsync(Path.Combine(directory, team ? "rating-team.png" : "rating-individual.png"), rating);
    }

    private static async Task CheckLayoutAsync(PlaywrightBrowserProvider provider, CaptureRenderer capture)
    {
        await using var context = await provider.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1200, Height = capture.Height }
        });
        var page = await context.NewPageAsync();
        // Fonts are local file URLs, so use the same navigation path as production.
        var path = Path.Combine(Path.GetTempPath(), $"multiplayer-test-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(path, capture.Html);
            await page.GotoAsync(new Uri(path).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            var bottom = await page.Locator("footer").EvaluateAsync<double>("el => el.getBoundingClientRect().bottom");
            Assert.InRange(bottom, capture.Height - 2, capture.Height);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth === 1200"));
        }
        finally { File.Delete(path); }
    }

    private sealed class CaptureRenderer(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Height = height;
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }

    private sealed class EmptyImageCache : IImageCacheService
    {
        public Task<byte[]> GetAvatarAsync(string avatarUrl, int userId) => throw new IOException("Offline preview");
        public Task<byte[]?> GetUserBackgroundAsync(int userId) => throw new NotSupportedException();
        public Task<byte[]?> GetUserBannerAsync(string? bannerUrl, int userId) => throw new NotSupportedException();
        public Task SaveUserBackgroundAsync(int userId, byte[] data) => throw new NotSupportedException();
        public Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index) => throw new NotSupportedException();
    }
}
