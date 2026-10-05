namespace MintAPI.Services;

public interface IRenderService
{
    /// <summary>渲染 HTML；height 为 0 时按加载完成后的 body 内容高度截图。</summary>
    Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default);
}
