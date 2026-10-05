using MintAPI.Rendering.BeatmapTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using MintOsuApi;
using Newtonsoft.Json;

namespace MintAPI.Tests.Integration;

[Trait("Category", "Integration")]
public class BeatmapInfoRenderingTests
{
    private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(0, "default")]
    [InlineData(1, "default")]
    [InlineData(2, "default")]
    [InlineData(3, "default")]
    [InlineData(0, "yaowan")]
    public async Task Beatmap_keeps_all_existing_fields_and_renders_all_modes(int mode, string themeName)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var avatar = await PreviewBytes("BEATMAP_INFO_PREVIEW_AVATAR");
        var theme = new DefaultBeatmapTheme(capture, new AvatarCardImageCache(avatar),
            mode == 0 ? new PpCalculatorService() : new Calculator(), NullLogger<DefaultBeatmapTheme>.Instance, new BeatmapAnalysisService());
        var set = Set();
        var map = Map(0, mode); map.Beatmapset = set;
        var mapper = new User { Id = 452514, Username = "PixelGlory <script> & \" 中文谱师" };
        var file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu");
        var png = await theme.RenderBeatmapAsync(map, mapper, Pixel, file, themeName);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.DoesNotContain("<script>", capture.Html);
        foreach (var value in new[] { "&lt;script&gt;", "3881559", "1885198", "191", "5:35", "4.5", "5.0", "9.8", "2022-11-16 20:34:56", "Sample source" })
            Assert.Contains(value, capture.Html);
        if (themeName == "default")
        {
            Assert.Contains("1,861", capture.Html); Assert.Contains("417", capture.Html); Assert.Contains("2,900", capture.Html);
            foreach (var field in new[] { "SS PP", "Circles", "Sliders", "CS", "HP", "OD", "AR", "Ranked", "MAP", "SET", "来源", "谱师", "总时长" })
                Assert.Contains(field, capture.Html);
            Assert.DoesNotContain("gradient(", capture.Html);
            await Inspect(browser, capture, async page =>
            {
                Assert.True(await page.Locator(".identity > *").EvaluateAllAsync<bool>("els => els.every(e => {const b=e.getBoundingClientRect(),p=e.closest('.identity').getBoundingClientRect(); return b.top>=p.top && b.bottom<=p.bottom;})"));
                Assert.True(await page.Locator(".small-value, .attribute").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('footer').getBoundingClientRect().top)"));
                Assert.Equal(4, await page.Locator(".attribute").CountAsync());
                Assert.Equal(mode == 0 ? "56px" : "52px", await page.Locator(".title").EvaluateAsync<string>("e => getComputedStyle(e).fontSize"));
                if (mode == 0)
                {
                    Assert.Equal(1768, capture.Height);
                    Assert.Equal(4, await page.Locator(".fc-panel .analysis-cell").CountAsync());
                    Assert.Equal(3, await page.Locator(".component-cell").CountAsync());
                    Assert.Equal(5, await page.Locator(".mod-table tbody tr").CountAsync());
                    Assert.Equal(5, await page.Locator(".mod-table th").CountAsync());
                    Assert.DoesNotContain("速率", capture.Html);
                    Assert.DoesNotContain("秒采样", capture.Html);
                    Assert.Contains("683.83", capture.Html);
                    Assert.Contains("792.13", capture.Html);
                    Assert.Contains("3.7887", capture.Html);
                    Assert.True(await page.EvaluateAsync<bool>("document.querySelector('.performance').nextElementSibling.classList.contains('details')"));
                    Assert.True(await page.Locator(".pp-row, .analysis-row, .fc-panel, .component-panel, .mods-panel, .skills-panel, .details, .a-value, .mod-table td").EvaluateAllAsync<bool>("els => els.every(e => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1)"));
                    Assert.Equal(2, await page.Locator(".curve-panel polyline").CountAsync());
                }
                else
                {
                    Assert.Equal(900, capture.Height);
                    Assert.Equal(0, await page.Locator(".mod-table").CountAsync());
                }
            });
        }
        var output = Environment.GetEnvironmentVariable("BEATMAP_INFO_PREVIEW_DIR");
        if (output is not null)
        {
            set.Title = "Epitaph"; set.Artist = set.ArtistUnicode = "TEARS OF TRAGEDY"; set.Source = "Sample source";
            map.Version = "Elegy"; mapper.Username = "PixelGlory";
            png = await theme.RenderBeatmapAsync(map, mapper, await PreviewBytes("BEATMAP_INFO_PREVIEW_BG"), file, themeName);
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, $"beatmap-{mode}-{themeName}.png"), png);
            await File.WriteAllTextAsync(Path.Combine(output, $"beatmap-{mode}-{themeName}.html"), capture.Html);
        }
    }

    [Theory]
    [InlineData(1028484, 1, 4, 2, 5)]
    [InlineData(2118524, 2, 1, 0, 4)]
    [InlineData(1638954, 3, 1, 0, 3)]
    public async Task Native_ruleset_analysis_renders_real_maps_with_readable_groups(
        int beatmapId, int mode, int curves, int components, int modColumns)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"analysis-{beatmapId}.json"));
        var map = JsonConvert.DeserializeObject<Beatmap>(json, OsuClient.BuildJsonSettings())!;
        var assetDir = Environment.GetEnvironmentVariable("BEATMAP_ANALYSIS_ASSET_DIR");
        async Task<byte[]> Asset(string suffix) => assetDir is null ? Pixel : await File.ReadAllBytesAsync(Path.Combine(assetDir, $"{beatmapId}-{suffix}"));
        var mapper = new User { Id = map.UserId, Username = map.Beatmapset!.Creator };
        var theme = new DefaultBeatmapTheme(capture, new AvatarCardImageCache(await Asset("avatar.png")),
            new PpCalculatorService(), NullLogger<DefaultBeatmapTheme>.Instance, new BeatmapAnalysisService());
        var file = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{beatmapId}.osu");
        var png = await theme.RenderBeatmapAsync(map, mapper, await Asset("bg.jpg"), file);
        if (Environment.GetEnvironmentVariable("BEATMAP_INFO_PREVIEW_DIR") is { } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, $"analysis-{mode}.png"), png);
            await File.WriteAllTextAsync(Path.Combine(output, $"analysis-{mode}.html"), capture.Html);
        }
        Assert.Equal(mode, map.ModeInt);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.DoesNotContain("gradient(", capture.Html);
        Assert.DoesNotContain("秒采样", capture.Html);
        Assert.DoesNotContain("速率", capture.Html);
        Assert.Contains(map.Beatmapset.Title, capture.Html);
        await Inspect(browser, capture, async page =>
        {
            Assert.Equal(1768, capture.Height);
            Assert.Equal(curves, await page.Locator(".curve-panel polyline").CountAsync());
            Assert.Equal(components, await page.Locator(".component-cell").CountAsync());
            Assert.Equal(4, await page.Locator(".fc-panel .analysis-cell").CountAsync());
            Assert.Equal(5, await page.Locator(".mod-table tbody tr").CountAsync());
            Assert.Equal(modColumns, await page.Locator(".mod-table th").CountAsync());
            Assert.True(await page.Locator(".pp-row, .analysis-row, .fc-panel, .component-panel, .mods-panel, .skills-panel, .details, .a-value, .mod-table td, .reference-detail, .composition-head, .skill-cell").EvaluateAllAsync<bool>("els => els.every(e => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1)"));
            Assert.True(await page.Locator(".analysis-row > section").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('footer').getBoundingClientRect().top)"));
            Assert.True(await page.EvaluateAsync<bool>("document.querySelector('.performance').nextElementSibling.classList.contains('details')"));
            if (mode == 1)
            {
                Assert.Equal(4, await page.Locator(".skill-cell").CountAsync());
                Assert.Contains("准确率", await page.Locator(".component-panel").InnerTextAsync());
            }
            else if (mode == 2)
            {
                Assert.Contains("漏小水滴", await page.Locator(".fc-panel").InnerTextAsync());
                Assert.Contains("1 Miss", await page.Locator(".fc-panel").InnerTextAsync());
                Assert.Equal(3, await page.Locator(".composition-head").CountAsync());
            }
            else
            {
                Assert.Contains("仅 320 / 200", await page.Locator(".fc-panel").InnerTextAsync());
                Assert.Equal(2, await page.Locator(".composition-head").CountAsync());
                Assert.Contains("LN 占比", await page.Locator(".skills-panel").InnerTextAsync());
            }
        });

    }

    [Theory]
    [InlineData(1, "default")]
    [InlineData(3, "default")]
    [InlineData(20, "default")]
    [InlineData(23, "default")]
    [InlineData(0, "default")]
    [InlineData(3, "yaowan")]
    public async Task Beatmapset_keeps_sorted_difficulties_parameters_and_twenty_row_limit(int count, string themeName)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var theme = new DefaultBeatmapTheme(capture, new AvatarCardImageCache(Pixel), new Calculator(), NullLogger<DefaultBeatmapTheme>.Instance, new BeatmapAnalysisService());
        var set = Set();
        set.Beatmaps = Enumerable.Range(0, count).Select(i => Map(i, i % 4)).ToList();
        // Header hit length remains the first API map's hit length, while display order is by stars.
        set.Beatmaps.Reverse();
        var png = await theme.RenderBeatmapsetAsync(set, Pixel, themeName);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.DoesNotContain("<script>", capture.Html);
        foreach (var value in new[] { "1885198", "191", "Sample source", "2022-11-16 20:34:56", "&lt;script&gt;", "PixelGlory" })
            Assert.Contains(value, capture.Html);
        if (themeName == "default")
        {
            await Inspect(browser, capture, async page =>
            {
                Assert.Equal(Math.Min(count, 20), await page.Locator(".map-row").CountAsync());
                Assert.Equal(Math.Min(count, 20) * 4, await page.Locator(".attribute").CountAsync());
                if (count > 0)
                {
                    Assert.Equal("MAP 3881559", await page.Locator(".map-id").First.InnerTextAsync());
                    Assert.Equal("2,900×", await page.Locator(".combo strong").First.InnerTextAsync());
                    Assert.Equal("4.5", await page.Locator(".attribute-value").First.InnerTextAsync());
                    Assert.True(await page.Locator(".map-details").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= e.closest('.map-row').getBoundingClientRect().bottom)"));
                }
                Assert.Equal(count > 20 ? "+ 3" : "", count > 20 ? await page.Locator(".extra").InnerTextAsync() : "");
                Assert.True(await page.Locator(".map-row").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('footer').getBoundingClientRect().top)"));
            });
        }
        var output = Environment.GetEnvironmentVariable("BEATMAP_INFO_PREVIEW_DIR");
        if (output is not null)
        {
            set.Title = "Epitaph"; set.Artist = "TEARS OF TRAGEDY"; set.Creator = "PixelGlory"; set.Source = "Sample source";
            var names = new[] { "Easy", "Normal", "Hard", "Insane", "Elegy" };
            var sorted = set.Beatmaps.OrderBy(map => map.DifficultyRating).ToArray();
            for (var i = 0; i < sorted.Length; i++) sorted[i].Version = names[i % names.Length];
            png = await theme.RenderBeatmapsetAsync(set, await PreviewBytes("BEATMAP_INFO_PREVIEW_BG"), themeName);
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, $"beatmapset-{count}-{themeName}.png"), png);
            await File.WriteAllTextAsync(Path.Combine(output, $"beatmapset-{count}-{themeName}.html"), capture.Html);
        }
    }

    private static Beatmapset Set() => new()
    {
        Id = 1885198, Title = "星の世界 / A song for the end of a very long journey <script> & 中文曲名",
        Artist = "Lumen & 月白", ArtistUnicode = "Lumen & 月白", Creator = "PixelGlory <script>",
        Source = "Sample source & \" <script>", Bpm = 191,
        RankedDate = DateTimeOffset.Parse("2022-11-16T12:34:56Z")
    };
    private static Beatmap Map(int index, int mode) => new()
    {
        Id = 3881559 + index, BeatmapsetId = 1885198, ModeInt = mode, Mode = (GameMode)mode,
        DifficultyRating = 2 + index * .45, MaxCombo = 2900 + index, Version = "Another [Long Difficulty Name] 中文难度 <script> & Test",
        Status = RankStatus.Ranked, Cs = 4.5, Drain = 5, Accuracy = 9.8, Ar = 9.8,
        Bpm = 191, TotalLength = 335, HitLength = 308, CountCircles = 1861, CountSliders = 417
    };
    private static async Task<byte[]> PreviewBytes(string key) => Environment.GetEnvironmentVariable(key) is { } path ? await File.ReadAllBytesAsync(path) : Pixel;

    private static async Task Inspect(PlaywrightBrowserProvider browser, Capture capture, Func<IPage, Task> inspect)
    {
        await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = capture.Width, Height = capture.Height } });
        var page = await context.NewPageAsync();
        var path = Path.Combine(Path.GetTempPath(), "beatmap-info-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            await File.WriteAllTextAsync(path, capture.Html);
            await page.GotoAsync(new Uri(path).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            Assert.Equal($"{capture.Width}x{capture.Height}", await page.EvaluateAsync<string>("document.documentElement.scrollWidth + 'x' + document.documentElement.scrollHeight"));
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
            Assert.True(await page.Locator(".number, .small-value, .map-details, .summary-item").EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth + 1)"));
            await inspect(page);
        }
        finally { File.Delete(path); }
    }
    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html; Width = width; Height = height;
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }
    private sealed class Calculator : IPpCalculatorService
    {
        public PpResult CalculateFixed(Score score, string osuFilePath) => throw new NotSupportedException();
        public PpResult CalculateSs(string osuFilePath, int rulesetId, uint mods = 0) => new(792, 7.69, 2900);
        public PpResult Calculate(Score score, string osuFilePath) => throw new NotSupportedException();
        public (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string osuFilePath) => throw new NotSupportedException();
        public (double NewPp, int Position) FindOptimalNewPp(List<double> ppList, double desiredIncrease) => throw new NotSupportedException();
    }
}
