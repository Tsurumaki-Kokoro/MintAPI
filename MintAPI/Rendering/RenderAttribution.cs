using System.Globalization;
using MintAPI.Services;

namespace MintAPI.Rendering;

/// <summary>默认图片右下角的统一署名与北京时间绘制日期。</summary>
public static class RenderAttribution
{
    public static string Add(string html, int width, int height, DateTimeOffset? renderedAt = null)
    {
        var date = (renderedAt ?? DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(8))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var inset = width <= 512 ? 16 : width <= 1100 ? 36 : 48;
        var size = width <= 512 ? 11 : width <= 1100 ? 13 : 16;
        var placement = height > 0
            ? $"position:fixed;right:{inset}px;bottom:{(width <= 512 ? 2 : 5)}px;"
            : $"position:relative;display:block;width:max-content;margin:10px {inset}px 0 auto;padding-bottom:12px;text-align:right;";
        var stamp = $"<style>.mint-attribution{{{placement}font:400 {size}px/{(width <= 512 ? 12 : 18)}px Arial,'PingFang SC','Microsoft YaHei',sans-serif;color:#86868b;white-space:nowrap;letter-spacing:0;z-index:1;}}</style>"
            + $"<div class=\"mint-attribution\">Powered By MintAPI · 绘制日期 {date}</div>";
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (end < 0) end = html.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? html.Insert(end, stamp) : html + stamp;
    }

    public static Task<byte[]> RenderAsync(IRenderService renderer, string html, int width, int height,
        CancellationToken cancellationToken = default)
        => renderer.RenderHtmlAsync(Add(html, width, height), width, height, cancellationToken);
}
