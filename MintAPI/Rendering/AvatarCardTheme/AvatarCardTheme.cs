using System.Net;
using MintAPI.Services;
using MintOsuApi.Models;

namespace MintAPI.Rendering.AvatarCardTheme;

public sealed class AvatarCardTheme(IImageCacheService imageCache, IRenderService renderer)
{
    public async Task<byte[]> RenderAsync(User user, CancellationToken cancellationToken = default)
    {
        var avatar = await imageCache.GetAvatarAsync(user.AvatarUrl, user.Id).WaitAsync(cancellationToken);
        if (avatar.Length == 0) throw new InvalidOperationException("Avatar is unavailable");
        var country = user.CountryCode.ToUpperInvariant();
        var flagPath = country.Length == 2 && country.All(c => c is >= 'A' and <= 'Z')
            ? Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "flags", country + ".png") : null;
        var flag = flagPath != null && File.Exists(flagPath)
            ? $"<img class='flag' src='data:image/png;base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(flagPath, cancellationToken))}' alt='{WebUtility.HtmlEncode(country)}'>"
            : $"<span class='country'>{WebUtility.HtmlEncode(country)}</span>";
        var font = new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", "HarmonyOS_Sans_SC_Bold.ttf")).AbsoluteUri;
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            @font-face{font-family:Harmony;src:url('{{font}}')}
            *{box-sizing:border-box}html,body{margin:0;width:512px;height:512px;background:#EDE7F6;overflow:hidden}
            body{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:16px;color:#45276B;font-family:Harmony,Arial,sans-serif}
            .avatar{width:400px;height:400px;border-radius:32px;object-fit:cover;flex-shrink:0}
            .identity{display:flex;align-items:center;justify-content:center;gap:12px;max-width:480px;height:64px}
            .flag{width:64px;height:44px;object-fit:contain;flex-shrink:0}
            .country{font-size:32px;flex-shrink:0}.name{font-size:48px;line-height:64px;white-space:nowrap}
            </style></head><body>
            <img class="avatar" src="data:image/png;base64,{{Convert.ToBase64String(avatar)}}" alt="">
            <div class="identity">{{flag}}<span class="name">{{WebUtility.HtmlEncode(user.Username)}}</span></div>
            <script>
            // The shared renderer awaits fonts.ready before taking its screenshot.
            const ready = Promise.all([document.fonts.ready, ...Array.from(document.images, image => image.decode())]).then(() => {
                const name = document.querySelector('.name');
                const identity = document.querySelector('.identity');
                let size = 48;
                while (identity.scrollWidth > 480 && size > 1) name.style.fontSize = (--size) + 'px';
            });
            Object.defineProperty(document.fonts, 'ready', { value: ready });
            </script></body></html>
            """;
        return await renderer.RenderHtmlAsync(html, 512, 512, cancellationToken);
    }
}
