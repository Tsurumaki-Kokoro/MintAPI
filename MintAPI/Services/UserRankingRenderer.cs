using System.Globalization;
using System.Net;
using System.Text;
using MintAPI.Controllers;

namespace MintAPI.Services;

/// <summary>绑定用户排行榜 PNG 渲染。</summary>
public sealed class UserRankingRenderer(IRenderService renderer, IImageCacheService imageCache)
{
    private static readonly string[] ModeNames = ["osu!", "osu!taiko", "osu!catch", "osu!mania"];
    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string N(double? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture) ?? "—";

    public async Task<byte[]> RenderAsync(string platform, IReadOnlyList<UserModeRanking> rankings, bool topFive, CancellationToken ct)
    {
        var avatars = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var user in rankings.SelectMany(r => r.Users))
        {
            ct.ThrowIfCancellationRequested();
            if (!avatars.ContainsKey(user.OsuUid))
            {
                var bytes = !string.IsNullOrWhiteSpace(user.AvatarUrl) && int.TryParse(user.OsuUid, out var id)
                    ? await imageCache.GetAvatarAsync(user.AvatarUrl, id).WaitAsync(ct) : [];
                var mime = bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 ? "image/jpeg"
                    : bytes.Length >= 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' ? "image/gif"
                    : bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? "image/webp" : "image/png";
                avatars[user.OsuUid] = bytes.Length > 0 ? $"data:{mime};base64,{Convert.ToBase64String(bytes)}" : "";
            }
            var country = user.CountryCode.ToUpperInvariant();
            if (!flags.ContainsKey(country))
            {
                var path = country.Length == 2 && country.All(char.IsAsciiLetter)
                    ? Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "flags", country + ".png") : null;
                flags[country] = path != null && File.Exists(path)
                    ? "data:image/png;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(path, ct)) : "";
            }
        }
        var html = new StringBuilder("<!doctype html><html><head><meta charset='utf-8'><style>");
        html.Append("*{box-sizing:border-box}body{margin:0;padding:44px 48px;background:#f6f7f9;color:#202533;font:16px 'Arial','PingFang SC','Microsoft YaHei',sans-serif}"
            + "header{border-top:5px solid #7460d7;padding-top:24px;margin-bottom:32px}.eyebrow{font-size:13px;letter-spacing:3px;color:#7460d7;font-weight:700}"
            + "h1{font-size:34px;margin:10px 0 12px;letter-spacing:-1px}.subtitle,footer{color:#737b8c;font-size:14px}"
            + ".grid{display:grid;grid-template-columns:1fr;gap:32px}.grid.multi{grid-template-columns:1fr 1fr}section{min-width:0}"
            + ".mode{display:flex;justify-content:space-between;align-items:baseline;border-bottom:2px solid #252b39;padding-bottom:14px}h2{font-size:24px;margin:0}.count{color:#737b8c;font-size:13px}"
            + "table{width:100%;table-layout:fixed;border-collapse:collapse}th{font-size:11px;letter-spacing:1px;color:#737b8c;font-weight:500;height:42px;text-align:right}"
            + "td{height:76px;border-bottom:1px solid #dde1e8;text-align:right;font-variant-numeric:tabular-nums;font-size:17px}"
            + "th:first-child,td:first-child{width:7%;text-align:left;color:#9299a7;font-size:13px}th:nth-child(2),td:nth-child(2){width:40%;text-align:left;padding-right:12px}"
            + "th:nth-child(3){width:19%}th:nth-child(4){width:19%}th:nth-child(5){width:15%}"
            + ".player{display:flex;align-items:center;gap:12px;min-width:0}.avatar{width:44px;height:44px;object-fit:cover;border-radius:8px;flex-shrink:0;background:#e4e2f0}.avatar.fallback{display:flex;align-items:center;justify-content:center;color:#7460d7;font-weight:700}.details{flex:1;min-width:0}.heading{display:flex;align-items:center;gap:7px;min-width:0}.flag{width:22px;height:16px;object-fit:contain;flex-shrink:0}.name{flex:1;min-width:0;"
            + "font-weight:700;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}.identity{font-size:11px;color:#737b8c;margin-top:7px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis}"
            + ".leader .name,.leader .pp{color:#7460d7}.pp{font-weight:700}.acc{font-size:14px}.empty{padding:32px 0;color:#737b8c}footer{margin-top:30px;padding-top:16px;border-top:1px solid #dde1e8}</style></head><body>");
        html.Append($"<header><div class='eyebrow'>MINT / LEADERBOARD</div><h1>{(topFive ? "四模式 · TOP 5" : "玩家排行榜")}</h1><div class='subtitle'>{E(platform)} · 全球排名从高到低</div></header><main class='grid{(topFive ? " multi" : "")}'>");
        foreach (var ranking in rankings)
        {
            html.Append($"<section><div class='mode'><h2>{ModeNames[ranking.GameMode]}</h2><span class='count'>{ranking.Users.Count} 位玩家</span></div><table><thead><tr><th>#</th><th>PLAYER</th><th>RANK</th><th>PP</th><th>ACC</th></tr></thead><tbody>");
            for (var i = 0; i < ranking.Users.Count; i++)
            {
                var user = ranking.Users[i];
                var avatar = avatars[user.OsuUid];
                var country = user.CountryCode.ToUpperInvariant();
                var flag = flags[country];
                var avatarHtml = avatar.Length > 0 ? $"<img class='avatar' src='{avatar}' alt=''>"
                    : $"<span class='avatar fallback'>{E(user.Username.Length > 0 ? user.Username[..1] : "?")}</span>";
                var flagHtml = flag.Length > 0 ? $"<img class='flag' src='{flag}' alt='{E(country)}'>"
                    : $"<span class='country'>{E(country)}</span>";
                html.Append($"<tr class='{(i == 0 ? "leader" : "")}'><td>{i + 1:00}</td><td><div class='player'>{avatarHtml}<div class='details'><div class='heading'>{flagHtml}<div class='name'>{E(user.Username)}</div></div><div class='identity'>osu! {E(user.OsuUid)} · {E(platform)} {E(user.PlatformUid)}</div></div></div></td>"
                    + $"<td>{(user.Rank.HasValue ? "#" + N(user.Rank, "N0") : "—")}</td><td class='pp'>{N(user.Pp, "N2")}</td><td class='acc'>{N(user.Acc, "F2")}{(user.Acc.HasValue ? "%" : "")}</td></tr>");
            }
            html.Append("</tbody></table>");
            if (ranking.Users.Count == 0) html.Append("<div class='empty'>暂无玩家</div>");
            html.Append("</section>");
        }
        html.Append("</main><footer>RANK 全球排名 · ACC 准确率 · — 暂无数据</footer></body></html>");
        return await renderer.RenderHtmlAsync(html.ToString(), topFive ? 1440 : 1080, 0, ct);
    }
}
