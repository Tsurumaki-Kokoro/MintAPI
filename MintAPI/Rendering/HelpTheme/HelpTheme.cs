using MintAPI.Services;

namespace MintAPI.Rendering.HelpTheme;

/// <summary>Mint osu! 指令帮助横版海报。</summary>
public sealed class HelpTheme(IRenderService renderer)
{
    public const int Width = 2800;

    public async Task<byte[]> RenderAsync(CancellationToken cancellationToken = default)
    {
        var root = AppContext.BaseDirectory;
        var html = await File.ReadAllTextAsync(Path.Combine(root, "Rendering", "HelpTheme", "help.html"), cancellationToken);
        html = html.Replace("__FONT__", new Uri(Path.Combine(root, "wwwroot", "fonts", "HarmonyOS_Sans_SC_Regular.ttf")).AbsoluteUri)
            .Replace("__BACKGROUND__", new Uri(Path.Combine(root, "wwwroot", "assets", "help", "minthelp-background-help.png")).AbsoluteUri);
        return await RenderAttribution.RenderAsync(renderer, html, Width, 0, cancellationToken);
    }
}
