using MintAPI.Rendering;

namespace MintAPI.Tests.Unit;

public sealed class RenderAttributionTests
{
    [Theory]
    [InlineData("2026-10-05T15:59:59Z", "2026-10-05")]
    [InlineData("2026-10-05T16:00:00Z", "2026-10-06")]
    public void Drawing_date_uses_beijing_midnight(string utc, string date)
    {
        var html = RenderAttribution.Add("<html><body>content</body></html>", 1500, 1000, DateTimeOffset.Parse(utc));
        Assert.Contains("Powered By MintAPI · 绘制日期 " + date, html);
        Assert.Contains("content", html);
    }
}
