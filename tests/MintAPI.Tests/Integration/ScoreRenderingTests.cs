using MintAPI.Rendering.ScoreTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public class ScoreRenderingTests
{
    [Theory]
    [InlineData(0, "default")]
    [InlineData(1, "default")]
    [InlineData(2, "default")]
    [InlineData(3, "default")]
    [InlineData(0, "yaowan")]
    public async Task Score_templates_render_mode_judgements_and_escape_text(int mode, string themeName)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");
        var avatarPath = Environment.GetEnvironmentVariable("SCORE_PREVIEW_AVATAR");
        var avatar = avatarPath == null ? pixel : await File.ReadAllBytesAsync(avatarPath);
        var theme = new DefaultScoreTheme(capture, new AvatarCardImageCache(avatar), new Calculator(), NullLogger<DefaultScoreTheme>.Instance);
        var score = new Score
        {
            RulesetId = mode, Rank = MintOsuApi.Enums.Grade.A, Pp = 321.45, Accuracy = .9876, MaxCombo = 1234,
            TotalScore = 987654321, EndedAt = DateTimeOffset.Parse("2026-09-30T08:00:00Z"),
            Beatmap = new Beatmap { Id = 123, Version = "Another [Long Difficulty Name]", Cs = 4, Ar = 9.5, Accuracy = 8, Drain = 6, Bpm = 180, TotalLength = 215 },
            Beatmapset = new BeatmapsetCompact { Title = "星の世界 / A song for the end of a very long journey", Artist = "Lumen & 月白", Creator = "Aster" },
            Mods = [new NonLegacyMod { Acronym = "HD" }, new NonLegacyMod { Acronym = "NC" }, new NonLegacyMod { Acronym = "DT" }],
            Statistics = new Statistics { Great = 1200, Ok = 23, Meh = 3, Miss = 2, Perfect = 800, Good = 16, LargeTickHit = 200, SmallTickHit = 350, SmallTickMiss = 8 }
        };
        var user = new User { Id = 1, Username = "Aster <script>alert(1)</script>", CountryCode = "CN", IsSupporter = true, Statistics = new UserStatistics { CountryRank = 27 } };
        score.RankGlobal = 5;
        var png = await theme.RenderAsync(score, user, pixel, "fixture.osu", null, themeName, "RECENT PLAY · #2");
        Assert.Contains("&lt;script&gt;", capture.Html);
        Assert.DoesNotContain("<script>", capture.Html);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        Assert.Equal(themeName == "default", capture.Html.Contains("Powered By MintAPI"));
        if (themeName == "default")
        {
            Assert.Contains("321.45", capture.Html);
            Assert.Contains("aria-label=\"Supporter\"", capture.Html);
            Assert.Contains("Global #5 · Country #27", capture.Html);
            Assert.Equal(mode == 0, capture.Html.Contains("class=\"components\""));
            if (mode == 0)
            {
                Assert.Contains("180.12", capture.Html);
                Assert.Contains("120.34", capture.Html);
                Assert.Contains("45.67", capture.Html);
            }
            Assert.Contains(mode == 3 ? "MAX" : mode == 2 ? "TINY MISS" : "300", capture.Html);
            await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1500, Height = 720 } });
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(1500, capture.Height);
            await page.SetContentAsync(capture.Html);
            await RenderingAttributionAssertions.CheckAsync(page);
            Assert.Equal($"1500x{capture.Height}", await page.EvaluateAsync<string>("`${document.documentElement.scrollWidth}x${document.documentElement.scrollHeight}`"));
            Assert.InRange(await page.Locator("footer").EvaluateAsync<double>("e => e.getBoundingClientRect().bottom"), capture.Height - 29, capture.Height - 28);
            Assert.Equal(2, await page.Locator(".mod-art").CountAsync());
            Assert.Equal("A", await page.Locator(".grade").InnerTextAsync());
            Assert.Equal(0, await page.Locator(".grade-art").CountAsync());
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
            Assert.True(await page.Locator(".mapline").EvaluateAsync<bool>("e => e.getBoundingClientRect().bottom <= e.closest('.identity').getBoundingClientRect().bottom"));
            if (mode == 0)
            {
                foreach (var grade in new[] { MintOsuApi.Enums.Grade.SSH, MintOsuApi.Enums.Grade.F })
                {
                    score.Rank = grade;
                    await theme.RenderAsync(score, user, pixel, "fixture.osu", null);
                    await page.SetContentAsync(capture.Html);
                    await RenderingAttributionAssertions.CheckAsync(page);
                    Assert.Equal(grade.ToString(), await page.Locator(".grade").InnerTextAsync());
                    Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
                }
                score.Rank = MintOsuApi.Enums.Grade.A;
            }
        }
        var output = Environment.GetEnvironmentVariable("SCORE_PREVIEW_DIR");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            user.Username = "Aster";
            var imagePath = Environment.GetEnvironmentVariable("SCORE_PREVIEW_IMAGE");
            var cover = imagePath == null ? pixel : await File.ReadAllBytesAsync(imagePath);
            png = await theme.RenderAsync(score, user, cover, "fixture.osu", null, themeName, "RECENT PLAY · #2");
            await File.WriteAllBytesAsync(Path.Combine(output, $"score-{mode}-{themeName}.png"), png);
            user.Username = "Aster <script>alert(1)</script>";
        }
        if (mode == 0 && themeName == "default")
        {
            score.MaxCombo = 1623;
            score.Accuracy = (2232 * 300d + 43 * 100 + 2 * 50) / (2278 * 300d);
            score.Pp = 601.44;
            score.TotalScore = 98765420;
            score.Mods = [];
            score.Statistics = new Statistics { Great = 2232, Ok = 43, Meh = 2, Miss = 1 };
            score.Beatmap!.Id = 3881559;
            score.Beatmap.Version = "Elegy";
            score.Beatmap.Cs = 4.5f;
            score.Beatmap.Ar = 9.8f;
            score.Beatmap.Accuracy = 9.8f;
            score.Beatmap.Drain = 5;
            score.Beatmap.Bpm = 191;
            score.Beatmap.TotalLength = 335;
            score.Beatmap.CountCircles = 1861;
            score.Beatmap.CountSliders = 417;
            score.Beatmap.Status = MintOsuApi.Enums.RankStatus.Ranked;
            score.EndedAt = DateTimeOffset.Parse("2024-08-09T08:26:40Z");
            score.Beatmapset!.Title = "Epitaph";
            score.Beatmapset.Artist = "TEARS OF TRAGEDY";
            score.Beatmapset.Creator = "PixelGlory";
            var realTheme = new DefaultScoreTheme(capture, new AvatarCardImageCache(avatar), new PpCalculatorService(), NullLogger<DefaultScoreTheme>.Instance);
            var coverPath = Environment.GetEnvironmentVariable("SCORE_PREVIEW_IMAGE");
            var realCover = coverPath == null ? pixel : await File.ReadAllBytesAsync(coverPath);
            user.Username = "Molli";
            user.IsSupporter = false;
            user.Statistics = null;
            score.RankGlobal = null;
            var realPng = await realTheme.RenderAsync(score, user, realCover,
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"), null, "default", "BEST PLAY · #1");
            Assert.Contains("1,623×", capture.Html);
            Assert.Contains("/ 2,900×", capture.Html);
            Assert.Contains("2024-08-09 08:26:40 UTC", capture.Html);
            Assert.Contains("MAP 3881559", capture.Html);
            Assert.Contains("Circles 1861", capture.Html);
            Assert.Contains("Sliders 417", capture.Html);
            Assert.Contains("CS 4.5", capture.Html);
            Assert.Contains("OD 9.8", capture.Html);
            Assert.DoesNotContain("3,970,268,479", capture.Html);
            if (output != null)
            {
                await File.WriteAllBytesAsync(Path.Combine(output, "3881559-combo.png"), realPng);
                await File.WriteAllTextAsync(Path.Combine(output, "3881559-combo.html"), capture.Html);
                score.Mods = [new NonLegacyMod { Acronym = "HD" }, new NonLegacyMod { Acronym = "NC" }, new NonLegacyMod { Acronym = "DT" }];
                var apiPp = score.Pp;
                score.Pp = null;
                var recentPng = await realTheme.RenderAsync(score, user, realCover,
                    Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"), null, "default", "RECENT PLAY · #2");
                await File.WriteAllBytesAsync(Path.Combine(output, "recent-3881559-mods.png"), recentPng);
                score.Pp = apiPp;
                score.Mods = [];
            }
            user.Username = "Aster <script>alert(1)</script>";
        }
        if (mode == 0 && themeName == "default")
        {
            var list = await theme.RenderBestListAsync(Enumerable.Repeat(score, 5).ToList(), user, 3);
            Assert.Contains("BEST PLAYS · #3–7", capture.Html);
            Assert.Contains("&lt;script&gt;", capture.Html);
            await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1500, Height = 720 } });
            var page = await context.NewPageAsync();
            await page.SetViewportSizeAsync(1500, capture.Height);
            await page.SetContentAsync(capture.Html);
            await RenderingAttributionAssertions.CheckAsync(page);
            Assert.InRange(await page.Locator("footer").EvaluateAsync<double>("e => e.getBoundingClientRect().bottom"), capture.Height - 24, capture.Height);
            if (output != null)
            {
                user.Username = "Aster";
                list = await theme.RenderBestListAsync(Enumerable.Repeat(score, 5).ToList(), user, 3);
                await File.WriteAllBytesAsync(Path.Combine(output, "best-list.png"), list);
            }
        }
    }

    [Theory]
    [InlineData(false, 5, 0)]
    [InlineData(true, 5, 0)]
    [InlineData(true, 20, 3)]
    [InlineData(false, 1, 2)]
    public async Task Lists_keep_key_metadata_backgrounds_and_hide_removed_fields(bool recent, int count, int mode)
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        var avatarPath = Environment.GetEnvironmentVariable("SCORE_PREVIEW_AVATAR");
        var avatar = avatarPath == null ? [] : await File.ReadAllBytesAsync(avatarPath);
        var files = new ListFiles();
        var theme = new DefaultScoreTheme(capture, new AvatarCardImageCache(avatar), new Calculator(), NullLogger<DefaultScoreTheme>.Instance, files);
        var titles = new[] { "Epitaph", "星の世界 / A song for the end of a very long journey", "Blue Zenith", "Lachryma《Re:Queen'M》", "Freedom Dive" };
        var grades = new[] { MintOsuApi.Enums.Grade.A, MintOsuApi.Enums.Grade.S, MintOsuApi.Enums.Grade.SSH, MintOsuApi.Enums.Grade.F, MintOsuApi.Enums.Grade.B };
        var scores = Enumerable.Range(0, count).Select(i => new Score
        {
            RulesetId = mode, Rank = grades[i % 5], Passed = i % 5 != 3,
            Pp = i % 5 == 3 ? null : i % 5 == 2 ? 1234.56 : 601.44 - i * 24.8,
            Accuracy = .9862 - i % 5 * .02, MaxCombo = i % 5 == 2 ? 18341 : 1623 - i * 43,
            Beatmap = new Beatmap { Version = i % 5 == 1 ? "Another [Long Difficulty Name]" : "Elegy" },
            Beatmapset = new BeatmapsetCompact { Id = ListFiles.SetIds[i % 5], Title = titles[i % 5], Artist = i % 5 == 0 ? "TEARS OF TRAGEDY" : "Lumen & 月白" },
            Mods = i % 5 == 0 ? [] : [new NonLegacyMod { Acronym = "HD" }, new NonLegacyMod { Acronym = "NC" }, new NonLegacyMod { Acronym = "DT" }],
            EndedAt = DateTimeOffset.Parse("2026-09-30T08:00:00Z").AddMinutes(-i * 10),
            Statistics = i % 5 == 4 ? null : new Statistics { Miss = i % 5 == 3 ? 1234 : i % 3 },
            Weight = new Weight { Percentage = 95 - i * 3, Pp = 571.37 - i * 30 }
        }).ToList();
        var user = new User { Id = 1, Username = "Aster <script>alert(1)</script>", CountryCode = "CN", IsSupporter = true };
        var png = recent ? await theme.RenderRecentListAsync(scores, user, 3) : await theme.RenderBestListAsync(scores, user, 3);
        Assert.Contains("&lt;script&gt;", capture.Html);
        Assert.DoesNotContain("<script>", capture.Html);
        Assert.Equal(!recent, capture.Html.Contains("WEIGHT 95.0% · 571.37 pp"));
        Assert.Equal(recent && count > 3, capture.Html.Contains(">FAILED<"));
        Assert.DoesNotContain("2026-09-30 08:00:00", capture.Html);
        Assert.DoesNotContain("COMBO", capture.Html);
        Assert.DoesNotContain(">MISS<", capture.Html);
        Assert.DoesNotContain("<time>", capture.Html);
        Assert.Equal(Math.Min(count, 5), files.Calls.Count);
        Assert.All(files.Calls.Values, calls => Assert.Equal(1, calls));
        Assert.Contains(">NM<", capture.Html);
        Assert.DoesNotContain("HD · NC · DT", capture.Html);
        Assert.Contains(mode == 3 ? "osu!mania" : mode == 2 ? "osu!catch" : "CN · osu!", capture.Html);
        if (count > 3)
        {
            Assert.Contains(">SSH</span>", capture.Html);
            Assert.DoesNotContain("18,341", capture.Html);
            Assert.DoesNotContain("1,234", capture.Html);
            Assert.Contains(">—<", capture.Html);
        }
        await using var context = await browser.Browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1500, Height = capture.Height } });
        var page = await context.NewPageAsync();
        var tempHtml = Path.Combine(Path.GetTempPath(), $"score-list-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(tempHtml, capture.Html);
            await page.GotoAsync(new Uri(tempHtml).AbsoluteUri);
            await page.EvaluateAsync("document.fonts.ready");
            Assert.Equal(count, await page.Locator("article.row").CountAsync());
            Assert.Equal($"1500x{capture.Height}", await page.EvaluateAsync<string>("`${document.documentElement.scrollWidth}x${document.documentElement.scrollHeight}`"));
            Assert.True(await page.Locator(".song").EvaluateAllAsync<bool>("els => els.every(e => { const b = e.getBoundingClientRect(); const r = e.closest('.row').getBoundingClientRect(); return b.top >= r.top && b.bottom <= r.bottom; })"));
            Assert.True(await page.Locator(".pp-row").EvaluateAllAsync<bool>("els => els.every(e => { const r = e.closest('.results').getBoundingClientRect(); return Array.from(e.children).every(c => c.getBoundingClientRect().left >= r.left - 1); })"));
            Assert.Equal(count < 5 ? count : count / 5 * 3, await page.Locator("img.background").CountAsync());
            Assert.Equal(count < 5 ? 0 : count / 5 * 2, await page.Locator("img.background-fallback").CountAsync());
            Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
        }
        finally { File.Delete(tempHtml); }
        var output = Environment.GetEnvironmentVariable("SCORE_PREVIEW_DIR");
        if (output != null)
        {
            Directory.CreateDirectory(output);
            user.Username = "Molli";
            files.PreviewImages = true;
            var previewTitles = new[] { "Epitaph", "Diamond", "Louder than steel", "C18H27NO3(extend)", "Executioner" };
            var previewArtists = new[] { "TEARS OF TRAGEDY", "Toyosaki Aki", "ryu5150", "Team Grimoire", "Laur" };
            var previewVersions = new[] { "Elegy", "Insane", "NiNo's Extreme", "4K Capsaicin", "Harbinger of Death" };
            for (var i = 0; i < scores.Count; i++)
            {
                scores[i].Beatmapset!.Title = previewTitles[i % 5];
                scores[i].Beatmapset!.Artist = previewArtists[i % 5];
                scores[i].Beatmap!.Version = previewVersions[i % 5];
            }
            if (!recent)
                for (var i = 0; i < scores.Count; i++)
                {
                    scores[i].Pp = 601.44 - i * 24.8;
                    scores[i].Passed = true;
                    if (scores[i].Rank == MintOsuApi.Enums.Grade.F) scores[i].Rank = MintOsuApi.Enums.Grade.B;
                    scores[i].Weight!.Pp = scores[i].Pp!.Value * scores[i].Weight!.Percentage / 100;
                }
            if (count >= 5)
            {
                scores[0].Statistics = new Statistics { Miss = 1 };
                scores[1].Accuracy = .9912;
                scores[1].Statistics = new Statistics { Miss = 0 };
                scores[2].Accuracy = 1;
                scores[2].MaxCombo = 1834;
                scores[2].Statistics = new Statistics { Miss = 0 };
                scores[3].Statistics = new Statistics { Miss = recent ? 22 : 5 };
            }
            png = recent ? await theme.RenderRecentListAsync(scores, user, 3) : await theme.RenderBestListAsync(scores, user, 3);
            var name = $"{(recent ? "recent" : "bp")}-list-{count}-mode-{mode}";
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".png"), png);
        }
    }

    private sealed class ListFiles : IBeatmapFileService
    {
        public static readonly int[] SetIds = [1885198, 111760, 993306, 303998, 2369185];
        public System.Collections.Concurrent.ConcurrentDictionary<int, int> Calls { get; } = new();
        public bool PreviewImages { get; set; }
        public Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId) => throw new NotSupportedException();
        public Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null) => throw new NotSupportedException();
        public string GetBgFilename(string osuFilePath) => throw new NotSupportedException();
        public async Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default)
        {
            Calls.AddOrUpdate(setId, 1, (_, count) => count + 1);
            var root = Environment.GetEnvironmentVariable("SCORE_PREVIEW_BG_ROOT");
            if (PreviewImages && root is not null)
            {
                var directory = Path.Combine(root, setId.ToString());
                var path = Directory.EnumerateFiles(directory).First(file => Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".png" or ".jpeg");
                return await File.ReadAllBytesAsync(path, cancellationToken);
            }
            if (setId == SetIds[3]) throw new IOException("Offline");
            if (setId == SetIds[4]) return null;
            return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");
        }
    }

    private sealed class Calculator : IPpCalculatorService
    {
        public PpResult CalculateFixed(Score score, string osuFilePath) => throw new NotSupportedException();
        public PpResult Calculate(Score score, string path) => new(300, 6.42, 1500, 180.12, 120.34, 45.67);
        public (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string path) => (350.67, 400.12);
        public PpResult CalculateSs(string path, int mode, uint mods = 0) => throw new NotSupportedException();
        public (double NewPp, int Position) FindOptimalNewPp(List<double> pp, double increase) => throw new NotSupportedException();
    }

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            Height = height;
            return inner.RenderHtmlAsync(html, width, height, cancellationToken);
        }
    }
}
