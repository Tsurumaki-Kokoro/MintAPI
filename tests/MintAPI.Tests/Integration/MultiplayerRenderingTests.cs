using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintAPI.Controllers;
using MintAPI.Errors;
using MintAPI.Tests.TestDoubles;
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
        match.MatchInfo.Name = string.Concat(Enumerable.Repeat("比赛长标题：红队与蓝队的多人比赛 / Multiplayer Invitational · ", 4));
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

    [Fact]
    public async Task HTTP_trim_applies_to_history_and_rating_images_and_uses_problem_details_for_errors()
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await provider.StartAsync();
        var capture = new CaptureRenderer(new PlaywrightRenderer(provider));
        var match = MultiplayerDataTests.Sample(rounds: 17);
        for (var id = 3; id <= 25; id++)
            match.EventList[0].Game!.Scores.Add(new MintOsuApi.Models.LegacyScore { UserId = id, Score = 100 });
        var upstreamUnavailable = false;
        var api = new RecordingOsuApiService
        {
            MatchHandler = _ => upstreamUnavailable
                ? throw new HttpRequestException("upstream unavailable")
                : match
        };
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(MultiplayerController).Assembly);
        builder.Services.AddSingleton(new MultiplayerService(api));
        builder.Services.AddSingleton(new MultiplayerTheme(capture, new EmptyImageCache(), NullLogger<MultiplayerTheme>.Instance));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        http.DefaultRequestHeaders.Accept.ParseAdd("image/png");

        foreach (var trim in new[] { "", "-1,0", "2", "2147483648,0" })
        {
            using var malformed = await http.GetAsync($"/multiplayer/history?mp_id=12345&trim={trim}");
            await ApiErrorAssertions.AssertAsync(malformed, ErrorCatalog.InvalidArgument);
        }
        Assert.Equal(0, api.CallCount);
        using var empty = await http.GetAsync("/multiplayer/rating?mp_id=12345&trim=2147483647,2147483647");
        await ApiErrorAssertions.AssertAsync(empty, ErrorCatalog.InvalidArgument);
        using var invalidPage = await http.GetAsync("/multiplayer/history?mp_id=12345&trim=2,14&page=2");
        await ApiErrorAssertions.AssertAsync(invalidPage, ErrorCatalog.InvalidArgument);

        var directory = Environment.GetEnvironmentVariable("MULTIPLAYER_PREVIEW_DIR");
        foreach (var endpoint in new[] { "history", "rating" })
        {
            var url = $"/multiplayer/{endpoint}?mp_id=12345";
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, $"trim-before-{endpoint}.png"), await http.GetByteArrayAsync(url));
            }
            using var response = await http.GetAsync(url + "&trim=2%2C14");
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("1", response.Headers.GetValues("X-Page-Count").Single());
            var png = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
            Assert.Contains("已跳过开头 2 局、结尾 14 局", capture.Html);
            if (endpoint == "history")
            {
                Assert.Contains("round-index\">03</span>", capture.Html);
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(capture.Html, "<section>"));
            }
            await CheckLayoutAsync(provider, capture);
            if (directory is not null)
                await File.WriteAllBytesAsync(Path.Combine(directory, $"trim-after-{endpoint}.png"), png);
        }

        using var allRatings = await http.GetAsync("/multiplayer/rating?mp_id=12345&page=0");
        allRatings.EnsureSuccessStatusCode();
        Assert.Equal("0", allRatings.Headers.GetValues("X-Page").Single());
        Assert.Equal("1", allRatings.Headers.GetValues("X-Page-Count").Single());
        Assert.Contains("全部 25 位玩家", capture.Html);
        Assert.Equal(25, System.Text.RegularExpressions.Regex.Matches(capture.Html, "<td>\\d+</td>\\s*<td>\\s*<div class=\"player\">").Count);
        await CheckLayoutAsync(provider, capture);
        if (directory is not null)
            await File.WriteAllBytesAsync(Path.Combine(directory, "rating-unpaged.png"), await allRatings.Content.ReadAsByteArrayAsync());

        using var secondPage = await http.GetAsync("/multiplayer/rating?mp_id=12345&page=2");
        secondPage.EnsureSuccessStatusCode();
        Assert.Equal("2", secondPage.Headers.GetValues("X-Page-Count").Single());
        Assert.Contains("<td>25</td>", capture.Html);
        Assert.DoesNotContain("<td>24</td>", capture.Html);

        upstreamUnavailable = true;
        using var failed = await http.GetAsync("/multiplayer/history?mp_id=12345&trim=1,0");
        await ApiErrorAssertions.AssertAsync(failed, ErrorCatalog.OsuApiUnavailable);
    }

    private static async Task CheckLayoutAsync(PlaywrightBrowserProvider provider, CaptureRenderer capture)
    {
        await using var context = await provider.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1500, Height = capture.Height }
        });
        var page = await context.NewPageAsync();
        // Fonts are local file URLs, so use the same navigation path as production.
        var path = Path.Combine(Path.GetTempPath(), $"multiplayer-test-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(path, capture.Html);
            await page.GotoAsync(new Uri(path).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            await RenderingAttributionAssertions.CheckAsync(page);
            foreach (var section in await page.Locator("section").AllAsync())
            {
                if (await section.Locator(".team-score").CountAsync() == 0) continue;
                Assert.True(await section.EvaluateAsync<bool>("e => Math.abs(e.querySelector('.team-score').getBoundingClientRect().bottom - e.querySelector('.map-extra').getBoundingClientRect().bottom) < 1"));
                Assert.DoesNotContain("获胜", await section.Locator(".result").InnerTextAsync());
            }
            var bottom = await page.Locator("main").EvaluateAsync<double>("el => el.getBoundingClientRect().bottom");
            Assert.True(bottom <= await page.Locator(".mint-attribution").EvaluateAsync<double>("e => e.getBoundingClientRect().top"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth === 1500"));
        }
        finally { File.Delete(path); }
    }

    private sealed class CaptureRenderer(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            var png = await inner.RenderHtmlAsync(html, width, height, cancellationToken);
            Height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            return png;
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
