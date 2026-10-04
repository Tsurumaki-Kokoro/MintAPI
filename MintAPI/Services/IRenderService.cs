namespace MintAPI.Services;

public interface IRenderService
{
    Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default);
}
