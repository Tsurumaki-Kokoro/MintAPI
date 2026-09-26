using HitCircleAPI.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitCircleAPI.Tests.Integration;

/// <summary>需要本机已安装 Playwright 的 Chromium（<c>playwright install chromium</c>）。</summary>
[Trait("Category", "Integration")]
public class PlaywrightRendererTests
{
    [Fact]
    public async Task RenderHtmlAsync_when_not_started_throws()
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);

        // 预热之前不该悄悄降级成"惰性启动"，那正是要消灭的行为
        Assert.Throws<InvalidOperationException>(() => provider.Browser);
    }

    [Fact]
    public async Task RenderHtmlAsync_produces_png_at_the_requested_size()
    {
        await using var provider = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await provider.StartAsync();

        var renderer = new PlaywrightRenderer(provider);

        const string html = "<html><body style='margin:0;background:#c00'></body></html>";
        var png = await renderer.RenderHtmlAsync(html, 120, 60);

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
        Assert.Equal(120u, ReadBigEndianUInt32(png, 16));
        Assert.Equal(60u, ReadBigEndianUInt32(png, 20));
    }

    private static uint ReadBigEndianUInt32(byte[] data, int offset)
        => (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}
