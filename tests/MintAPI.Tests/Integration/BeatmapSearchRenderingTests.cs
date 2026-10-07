using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using MintAPI.Rendering.BeatmapSearchTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Tests.Integration;

public sealed class BeatmapSearchRenderingTests
{
    [Fact]
    public async Task Five_sets_render_compact_rows_with_icons_and_no_overflow()
    {
        var sets = new[] { 1638954, 2118524, 4313549, 5148257, 1028484 }.Select(id =>
            JsonConvert.DeserializeObject<Beatmap>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"analysis-{id}.json")), OsuClient.BuildJsonSettings())!)
            .Select(map => { var set = map.Beatmapset!; return new Beatmapset { Id = map.BeatmapsetId, Title = set.Title,
                TitleUnicode = set.TitleUnicode, Artist = set.Artist, ArtistUnicode = set.ArtistUnicode, Creator = set.Creator,
                Covers = set.Covers, Status = set.Status, Beatmaps = [map] }; }).ToArray();
        // Exercise all four glyphs, black high-star icons, overflow count and long titles.
        sets[4].Beatmaps = Enumerable.Range(0, 22).Select(i => new Beatmap { Id = i + 1, ModeInt = i % 4, DifficultyRating = i * .5 }).ToList();
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var capture = new Capture(new PlaywrightRenderer(browser));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var preview = Environment.GetEnvironmentVariable("MINT_SEARCH_PREVIEW_DIR");
        var theme = new BeatmapSearchTheme(capture, new Clients(preview is not null), cache, NullLogger<BeatmapSearchTheme>.Instance);
        var png = await theme.RenderAsync(sets, "谱面搜索 · 布局预览", "any", "any", 1, 1, 5);
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(900, 800);
        await page.SetContentAsync(capture.Html);
        await page.EvaluateAsync("document.fonts.ready");
        Assert.Equal(5, await page.Locator(".row").CountAsync());
        Assert.Equal(22, await page.Locator(".difficulty svg").CountAsync());
        Assert.Equal("+4", await page.Locator(".more").InnerTextAsync());
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.scrollWidth === 900 && e.getBoundingClientRect().height < 650"));
        Assert.True(await page.Locator(".difficulties").EvaluateAllAsync<bool>("els => els.every(e => e.scrollWidth <= e.clientWidth)"));
        Assert.Contains("Powered By MintAPI", capture.Html);
        if (preview is not null)
        {
            Directory.CreateDirectory(preview);
            await File.WriteAllBytesAsync(Path.Combine(preview, "beatmap-search.png"), png);
            await File.WriteAllTextAsync(Path.Combine(preview, "beatmap-search.html"), capture.Html);
        }
        var empty = await theme.RenderAsync([], "没有结果", "any", "any", 1, 1, 0);
        Assert.NotEmpty(empty);
        Assert.Contains("没有找到匹配的谱面", capture.Html);
        await theme.RenderAsync(sets.Take(1).ToArray(), "<img src=x onerror=alert(1)>", "any", "any", 1, 1, 1);
        Assert.Contains("&lt;img", capture.Html);
        Assert.DoesNotContain("<img src=x", capture.Html);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 2)]
    public async Task Image_pages_preserve_all_sets_and_return_navigation_headers(int page, int count)
    {
        var sets = Enumerable.Range(1, 7).Select(i => new Beatmapset { Id = i, Title = $"Set {i}" }).ToList();
        var api = new RecordingOsuApiService { SearchHandler = (_, _, _, _, _) => new BeatmapsetSearchResult { Total = 20, Beatmapsets = sets, CursorString = "next" } };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var capture = new Capture(null);
        var theme = new BeatmapSearchTheme(capture, new Clients(false), cache, NullLogger<BeatmapSearchTheme>.Instance);
        var controller = new BeatmapSearchController(api, cache, NullLogger<BeatmapSearchController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var response = Assert.IsType<FileContentResult>(await controller.SearchImage(theme, "song", page: page));
        Assert.Equal("image/png", response.ContentType);
        Assert.Equal("2", controller.Response.Headers["X-Page-Count"]);
        Assert.Equal("next", controller.Response.Headers["X-Next-Cursor"]);
        Assert.Equal(count, capture.Html.Split("<article class='row'>").Length - 1);
        Assert.IsType<BadRequestObjectResult>(await controller.SearchImage(theme, "song", page: 3));
        Assert.Equal(1, api.CallCount);
    }

    private sealed class Capture(IRenderService? inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            return inner?.RenderHtmlAsync(html, width, height, cancellationToken) ?? Task.FromResult(new byte[] { 1 });
        }
    }

    private sealed class Clients(bool live) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => live ? new HttpClient() : new HttpClient(new Missing());
    }
    private sealed class Missing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
