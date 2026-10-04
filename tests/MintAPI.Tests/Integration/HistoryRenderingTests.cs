using MintAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class HistoryRenderingTests
{
    [Fact]
    public async Task History_images_render_as_png()
    {
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync();
        var renderer = new HistoryRenderer(new PlaywrightRenderer(browser));
        var date = new DateOnly(2026, 9, 1);
        var trend = await renderer.RenderTrendAsync("玩家 PP / Rank", [new(date, 100, 1000, "local"), new(date.AddDays(2), 120, 900, "osutrack")], default);
        var scores = await renderer.RenderScoresAsync("玩家 · Beatmap 1", "本地记录", [new Score { EndedAt = DateTimeOffset.UtcNow, TotalScore = 1000, Accuracy = .95, MaxCombo = 100, Passed = false }], 0, 1, default);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, trend[..4]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, scores[..4]);
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mintapi-history-preview"));
        await File.WriteAllBytesAsync(Path.Combine(Path.GetTempPath(), "mintapi-history-preview", "trend.png"), trend);
        await File.WriteAllBytesAsync(Path.Combine(Path.GetTempPath(), "mintapi-history-preview", "scores.png"), scores);
    }
}
