using System.Collections.Concurrent;
using MintAPI.Rendering.PerformanceAnalyzeTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

[Trait("Category", "Integration")]
public class PerformanceAnalyzeRenderingTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(3)]
    [InlineData(0)]
    public async Task Report_renders_top_ten_covers_combo_and_short_or_empty_results(int count)
    {
        var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");
        var previewDirectory = Environment.GetEnvironmentVariable("BP_ANALYSIS_PREVIEW_DIR");
        var avatarPath = Environment.GetEnvironmentVariable("BP_ANALYSIS_PREVIEW_AVATAR");
        var avatar = avatarPath is null ? pixel : await File.ReadAllBytesAsync(avatarPath);
        var files = new Covers(pixel, Environment.GetEnvironmentVariable("BP_ANALYSIS_PREVIEW_BG_ROOT"));
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var theme = new PerformanceAnalyzeTheme(capture, new AvatarCardImageCache(avatar), files,
            new PpCalculatorService(), NullLogger<PerformanceAnalyzeTheme>.Instance);
        var titles = new[] { "Epitaph", "Diamond", "Louder than steel", "C18H27NO3(extend)", "Executioner", "Blue Zenith", "Freedom Dive", "Raise My Sword", "The Sun The Moon", "Ghost Rule" };
        var versions = new[] { "Elegy", "Insane", "NiNo's Extreme", "Extended", "Ascension", "Four Dimensions", "Another", "Heavenly", "Insane", "Extra" };
        var mappers = new[] { "PixelGlory", "Sotarks", "Monstrata", "Ryuusei Aika", "reform", "Mapper A", "Mapper B" };
        var grades = new[] { Grade.SSH, Grade.SH, Grade.S, Grade.A, Grade.B };
        var scores = Enumerable.Range(0, count).Select(index => new Score
        {
            Pp = 620 - 420 * Math.Pow(index / 99.0, 0.58),
            Accuracy = .965 + (index % 10) * .0035,
            MaxCombo = 125 + (index * 137) % 3000,
            Rank = grades[index % grades.Length],
            Mods = index % 3 == 0 ? [] : [new NonLegacyMod { Acronym = "HD" }],
            Beatmap = new Beatmap
            {
                Id = index + 1, BeatmapsetId = Covers.SetIds[index % Covers.SetIds.Length],
                DifficultyRating = 5.2 + (index % 15) * .2, Bpm = 150 + (index % 10) * 10,
                TotalLength = 160 + index % 90, Version = versions[index % 10]
            },
            Beatmapset = new BeatmapsetCompact
            {
                Id = Covers.SetIds[index % Covers.SetIds.Length],
                Title = titles[index % 10], Creator = mappers[index % mappers.Length]
            }
        }).ToArray();
        if (count > 0)
            scores[0].Beatmapset!.Title = "A very long 中文曲名 <script>alert(1)</script> & another extremely long title";
        var user = new User { Id = 100001, Username = "Aster <script>alert(1)</script>", CountryCode = "CN" };

        // API ordering must not turn the top-ten column into an arbitrary ten scores.
        var png = await theme.RenderAsync(user, scores.Reverse().ToArray(), "osu!");
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.Equal(Math.Min(count, 10), CountOccurrences(capture.Html, "<article class=\"play-row\""));
        Assert.DoesNotContain("<script>", capture.Html);
        Assert.Contains("&lt;script&gt;", capture.Html);
        Assert.DoesNotContain("BP 数量", capture.Html);
        Assert.DoesNotContain("原始 PP", capture.Html);
        Assert.DoesNotContain("合计", capture.Html);
        Assert.DoesNotContain("总计", capture.Html);
        Assert.DoesNotContain("时间分布", capture.Html);
        Assert.Contains("Combo 分布", capture.Html);
        Assert.Equal(Math.Min(count, Covers.SetIds.Length), files.Calls.Count);
        Assert.All(files.Calls.Values, calls => Assert.Equal(1, calls));

        await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 2000, Height = 1800 }
        });
        var page = await context.NewPageAsync();
        var htmlPath = Path.Combine(Path.GetTempPath(), $"bp-analysis-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(htmlPath, capture.Html);
            await page.GotoAsync(new Uri(htmlPath).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            Assert.Equal("2000x1800", await page.EvaluateAsync<string>("document.documentElement.scrollWidth + 'x' + document.documentElement.scrollHeight"));
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
            Assert.True(await page.Locator(".song").EvaluateAllAsync<bool>("els => els.every(e => { const b=e.getBoundingClientRect(), r=e.closest('.play-row').getBoundingClientRect(); return b.top>=r.top && b.bottom<=r.bottom; })"));
            Assert.True(await page.Locator(".play-pp").EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth + 1)"),
                await page.Locator(".play-pp").EvaluateAllAsync<string>("els => JSON.stringify(els.map(e=>({text:e.textContent,width:e.clientWidth,scroll:e.scrollWidth,font:getComputedStyle(e).fontSize})))"));
            Assert.True(await page.Locator(".histogram").EvaluateAllAsync<bool>("els => els.every(e => { const l=Array.from(e.querySelectorAll('.hist-label')); return l.every((t,i)=>i===0 || l[i-1].getBoundingClientRect().right <= t.getBoundingClientRect().left); })"));
            Assert.True(await page.Locator(".play-row").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('footer').getBoundingClientRect().top)"));
            if (count > 0)
            {
                Assert.Equal("620.00 pp", await page.Locator(".play-pp").First.InnerTextAsync());
                Assert.Equal(3, await page.Locator(".distributions h2").CountAsync());
                Assert.Equal(count == 3 ? "#3" : "#100", await page.Locator(".curve .axis-text").Last.TextContentAsync());
            }
            if (count == 100 && previewDirectory is null)
                Assert.Equal(2, await page.Locator(".background-fallback").CountAsync());
        }
        finally { File.Delete(htmlPath); }

        if (previewDirectory is not null)
        {
            Directory.CreateDirectory(previewDirectory);
            user.Username = "Aster";
            if (count > 0) scores[0].Beatmapset!.Title = titles[0];
            png = await theme.RenderAsync(user, scores, "osu!");
            await File.WriteAllBytesAsync(Path.Combine(previewDirectory, $"analysis-{count}.png"), png);
            var previewHtml = capture.Html.Replace($"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}", "/wwwroot");
            await File.WriteAllTextAsync(Path.Combine(previewDirectory, $"analysis-{count}.html"), previewHtml);
        }
    }

    private static int CountOccurrences(string html, string text) => html.Split(text).Length - 1;

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            Assert.Equal(2000, width);
            Assert.Equal(1800, height);
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }

    private sealed class Covers(byte[] pixel, string? previewRoot) : IBeatmapFileService
    {
        public static readonly int[] SetIds = [1885198, 111760, 993306, 303998, 2369185, 543250, 728276];
        public ConcurrentDictionary<int, int> Calls { get; } = new();
        public async Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default)
        {
            Calls.AddOrUpdate(setId, 1, (_, value) => value + 1);
            if (previewRoot is not null)
            {
                var directory = Path.Combine(previewRoot, setId.ToString());
                var images = Directory.EnumerateFiles(directory).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png");
                var path = images.OrderBy(path => Path.GetFileName(path) == "list-cover.jpg" ? 0 : 1).First();
                return await File.ReadAllBytesAsync(path, cancellationToken);
            }
            if (setId == SetIds[4]) throw new IOException("Offline");
            return setId == SetIds[5] ? null : pixel;
        }
        public Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId) => throw new NotSupportedException();
        public Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null) => throw new NotSupportedException();
        public string GetBgFilename(string osuFilePath) => throw new NotSupportedException();
    }
}
