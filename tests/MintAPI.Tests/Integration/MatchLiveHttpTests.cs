using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using MintAPI.Rendering.MultiplayerTheme;
using MintAPI.Services;
using MintAPI.Services.MatchLive;
using MintAPI.Tests.TestDoubles;
using MintAPI.Tests.Unit;
using Microsoft.Playwright;

namespace MintAPI.Tests.Integration;

public class MatchLiveHttpTests
{
    [Fact]
    public async Task HTTP_mock_lifecycle_renders_real_six_player_results_and_recovers_updates()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var renderer = new Capture(new PlaywrightRenderer(browser));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(MatchLiveController).Assembly);
        builder.Services.AddSingleton(MatchLiveTests.Service(new RecordingOsuApiService(), new MatchLiveTests.Store(), new MatchLiveTests.Clock(), true));
        builder.Services.AddSingleton(new MultiplayerTheme(renderer, new EmptyImages(), NullLogger<MultiplayerTheme>.Instance));
        await using var app = builder.Build();
        app.MapControllers(); app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var created = await http.PostAsJsonAsync("/multiplayer/live/subscriptions", new { matchId = 109975520, scope = "bot:qq:123" });
        created.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = document.RootElement.GetProperty("subscriptionId").GetString();
        Assert.Equal("waiting", document.RootElement.GetProperty("snapshot").GetProperty("status").GetString());
        var advance = await http.PostAsync("/multiplayer/live/mock/109975520/advance", null);
        advance.EnsureSuccessStatusCode();
        using var started = JsonDocument.Parse(await advance.Content.ReadAsStringAsync());
        var gameId = started.RootElement.GetProperty("currentGame").GetProperty("id").GetInt32();
        var path = $"/multiplayer/live/109975520/games/{gameId}/image";
        var startImage = await http.GetAsync(path);
        startImage.EnsureSuccessStatusCode();
        Assert.Equal("true", startImage.Headers.GetValues("X-MatchLive-Mock").Single());
        Assert.Contains("模拟重放", renderer.Html); Assert.Contains("正在进行", renderer.Html);
        await LayoutAndSave(browser, renderer, await startImage.Content.ReadAsByteArrayAsync(), "matchlive-start.png", 0);
        (await http.PostAsync("/multiplayer/live/mock/109975520/advance", null)).EnsureSuccessStatusCode();
        var endImage = await http.GetAsync(path);
        endImage.EnsureSuccessStatusCode();
        Assert.Contains("对局结算", renderer.Html);
        await LayoutAndSave(browser, renderer, await endImage.Content.ReadAsByteArrayAsync(), "matchlive-finished.png", 6);
        (await http.PostAsync("/multiplayer/live/mock/109975520/advance", null)).EnsureSuccessStatusCode();
        var updates = await http.GetFromJsonAsync<JsonElement>($"/multiplayer/live/subscriptions/{id}/updates?after=0");
        Assert.Equal(new[] { "game-started", "game-finished", "room-closed" }, updates.GetProperty("updates").EnumerateArray().Select(u => u.GetProperty("type").GetString()));
        Assert.Equal("closed", updates.GetProperty("snapshot").GetProperty("status").GetString());
        var cursor = updates.GetProperty("cursor").GetInt64();
        var empty = await http.GetFromJsonAsync<JsonElement>($"/multiplayer/live/subscriptions/{id}/updates?after={cursor}");
        Assert.Equal(0, empty.GetProperty("updates").GetArrayLength());
        var invalid = await http.GetAsync($"/multiplayer/live/subscriptions/{id}/updates?after={cursor + 1}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        (await http.PostAsync("/multiplayer/live/mock/109975520/reset", null)).EnsureSuccessStatusCode();
        var reset = await http.GetFromJsonAsync<JsonElement>($"/multiplayer/live/subscriptions/{id}/updates?after={cursor}");
        Assert.Equal("mock-reset", reset.GetProperty("updates")[0].GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/multiplayer/live/subscriptions/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await http.GetAsync($"/multiplayer/live/subscriptions/{id}/updates")).StatusCode);
    }

    private static async Task LayoutAndSave(PlaywrightBrowserProvider browser, Capture renderer, byte[] png, string name, int playerCount)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png[..4]);
        await using var context = await browser.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1500, renderer.Height);
        var htmlPath = Path.Combine(Path.GetTempPath(), $"matchlive-layout-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlPath, renderer.Html);
        try { await page.GotoAsync(new Uri(htmlPath).AbsoluteUri); }
        finally { File.Delete(htmlPath); }
        await page.EvaluateAsync("document.fonts.ready");
        Assert.Equal(playerCount, await page.Locator("tbody tr").CountAsync());
        Assert.True(await page.Locator("img").EvaluateAllAsync<bool>("els => els.every(e => e.complete && e.naturalWidth > 0)"));
        Assert.InRange(await page.Locator("main").EvaluateAsync<double>("e => e.getBoundingClientRect().bottom"), renderer.Height - 1, renderer.Height);
        Assert.Equal(1500, await page.EvaluateAsync<int>("document.documentElement.scrollWidth"));
        var directory = Environment.GetEnvironmentVariable("MULTIPLAYER_PREVIEW_DIR");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name), png);
            await File.WriteAllTextAsync(Path.Combine(directory, name.Replace(".png", ".html")), renderer.Html);
        }
    }

    private sealed class Capture(IRenderService inner) : IRenderService
    {
        public string Html { get; private set; } = "";
        public int Height { get; private set; }
        public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        { Html = html; var png = await inner.RenderHtmlAsync(html, width, height, cancellationToken); Height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)); return png; }
    }
    private sealed class EmptyImages : IImageCacheService
    {
        public Task<byte[]> GetAvatarAsync(string url, int userId) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]?> GetUserBannerAsync(string? url, int userId) => Task.FromResult<byte[]?>(null);
        public Task<byte[]> GetBadgeAsync(string url, int userId, int index) => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]?> GetUserBackgroundAsync(int userId) => Task.FromResult<byte[]?>(null);
        public Task SaveUserBackgroundAsync(int userId, byte[] data) => Task.CompletedTask;
    }
}
