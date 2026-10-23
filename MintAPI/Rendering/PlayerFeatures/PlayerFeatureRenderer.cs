using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintAPI.Services.PlayerFeatures;
using MintOsuApi.Models;

namespace MintAPI.Rendering.PlayerFeatures;

public sealed class PlayerFeatureRenderer(IRenderService renderer, IImageCacheService images,
    IBeatmapFileService maps, ILogger<PlayerFeatureRenderer> logger)
{
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
    private static string N(double? n, string format = "N2") => n.HasValue && double.IsFinite(n.Value) ? n.Value.ToString(format, CultureInfo.InvariantCulture) : "—";
    private static string R(int? rank) => rank is > 0 ? "#" + N(rank, "N0") : "—";
    public static string ModeName(int mode) => mode switch { 1 => "osu!taiko", 2 => "osu!catch", 3 => "osu!mania", _ => "osu!standard" };
    public static string Mods(Score s) => s.Mods is { Count: > 0 } ? "+" + string.Join(",", s.Mods.Select(m => m.Acronym)) : "NM";
    private static string UriData(byte[]? bytes) => bytes is not { Length: > 0 } ? "" :
        $"data:{(bytes[0] == 0x89 ? "image/png" : bytes[0] == 0x52 ? "image/webp" : "image/jpeg")};base64,{Convert.ToBase64String(bytes)}";
    private async Task<string> AvatarAsync(User user, CancellationToken ct) => UriData(await images.GetAvatarAsync(user.AvatarUrl, user.Id).WaitAsync(ct));
    private static string Identity(User user, string avatar, string subtitle, bool right = false) =>
        $"<div class='identity {(right ? "player-b" : "")}'><div class='avatar'>{(avatar.Length > 0 ? $"<img src='{avatar}' alt=''>" : E(user.Username.Length > 0 ? user.Username[..1] : "?"))}</div><div><div class='name'>{E(user.Username)}</div><div class='muted'>{E(subtitle)}</div></div></div>";
    private static string Header(string label, string cover, string content) =>
        $"<header class='profile'>{(cover.Length > 0 ? $"<img class='profile-cover' src='{cover}' alt=''>" : "")}<div class='profile-wash'></div><div class='top'><div class='eyebrow'>MINTAPI / {label}</div></div>{content}</header>";

    private async Task<string> BackgroundAsync(Score score, CancellationToken ct)
    {
        var setId = score.Beatmapset?.Id ?? score.Beatmap?.BeatmapsetId ?? 0;
        if (setId <= 0) return "";
        try { return UriData(await maps.GetListCoverAsync(setId, ct)); }
        catch (RetryableException) { throw; }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { logger.LogWarning(ex, "Player report background unavailable for set {SetId}", setId); return ""; }
    }

    public async Task<byte[]> WeeklyAsync(WeeklyReport data, CancellationToken ct)
    {
        var user = data.User;
        var banner = UriData(await images.GetUserBannerAsync(user.Cover?.Url ?? user.Cover?.CustomUrl, user.Id).WaitAsync(ct));
        if (banner.Length == 0) banner = UriData(await images.GetUserBackgroundAsync(user.Id).WaitAsync(ct));
        var sb = new StringBuilder(Header("WEEKLY REPORT", banner,
            $"<div class='top'>{Identity(user, await AvatarAsync(user, ct), $"{ModeName(data.GameMode)} · {user.CountryCode}")}<div class='right'><h1>个人周报</h1><div class='muted'>{data.Start:yyyy.MM.dd} — {data.End.AddDays(-1):MM.dd}</div></div></div>"));
        var a = data.Baseline; var b = data.Final;
        var pp = a?.Pp.HasValue == true && b?.Pp.HasValue == true ? b.Pp - a.Pp : null;
        double? rank = a?.GlobalRank is > 0 && b?.GlobalRank is > 0 ? a.GlobalRank - b.GlobalRank : null;
        double? plays = a?.PlayCount.HasValue == true && b?.PlayCount >= a.PlayCount ? b.PlayCount - a.PlayCount : null;
        double? time = a?.PlayTime.HasValue == true && b?.PlayTime >= a.PlayTime ? b.PlayTime - a.PlayTime : null;
        string Metric(string label, string value, string unit, string sub, bool accent = false) =>
            $"<div><div class='muted'>{label}</div><div class='value {(accent ? "accent" : "")}'>{value} <small>{unit}</small></div><div class='sub'>{sub}</div></div>";
        sb.Append("<div class='metrics'>");
        sb.Append(Metric("总 PP 变化", N(pp, "+0.00;-0.00;0.00"), "pp", $"{N(a?.Pp)} → {N(b?.Pp)}", true));
        sb.Append(Metric(rank is < 0 ? "全球排名下降" : "全球排名提升", N(rank.HasValue ? Math.Abs(rank.Value) : null, "N0"), "名", $"{R(a?.GlobalRank)} → {R(b?.GlobalRank)}"));
        sb.Append(Metric("游玩次数增加", N(plays, "N0"), "次", $"游玩时长 +{(time.HasValue ? $"{(int)time.Value / 3600} 小时 {(int)time.Value / 60 % 60} 分" : "—")}"));
        sb.Append(Metric("新增 BP", data.BestCoverage ? data.NewBestPlays.Count.ToString() : "—", "条",
            data.NewBestPlays.Count > 0 ? $"本周最高 {N(data.NewBestPlays.Max(x => x.Score.Pp))} pp" : data.BestCoverage ? "本周无新增 BP" : "缺少期末 BP 快照"));
        sb.Append("</div><div class='columns'><section><h2>PP 趋势 <span class='muted'>/ 每日快照</span></h2>");
        sb.Append(Chart(data));
        var complete = data.Points.Count == 8 && data.Points.All(x => x.PlayCount.HasValue);
        var active = complete ? data.Points.Zip(data.Points.Skip(1)).Count(x => x.Second.PlayCount > x.First.PlayCount).ToString() : "—";
        sb.Append($"<div class='line'><span class='muted'>活跃天数</span><strong>{active} / 7 天</strong></div></section><section><h2>本周新增 BP <span class='muted'>/ {(data.BestCoverage ? $"{data.NewBestPlays.Count} 条 · 展示前 {Math.Min(3, data.NewBestPlays.Count)} 条" : "期末快照未知")}</span></h2><div class='bp-table'>");
        foreach (var best in data.NewBestPlays.Take(3))
        {
            var score = best.Score;
            var cover = await BackgroundAsync(score, ct);
            var set = score.Beatmapset ?? score.Beatmap?.Beatmapset;
            sb.Append($"<div class='bp-row'>{(cover.Length > 0 ? $"<img class='bp-background' src='{cover}' alt=''>" : "")}<div><div class='title'>{E(set?.Title)} <span class='tag'>{E(Mods(score))}</span></div><div class='muted'>{E(score.Beatmap?.Version)} · {N(score.Accuracy * 100)}% · {(score.Statistics?.Miss.HasValue == true ? score.Statistics.Miss + " miss" : "— miss")}</div></div><div class='right'><strong class='accent'>{N(score.Pp)} pp</strong><div class='sub'>BP #{best.Position}</div></div></div>");
        }
        if (data.NewBestPlays.Count == 0) sb.Append($"<div class='empty'>{(data.BestCoverage ? "本周没有新增 BP" : "尚无期末 BP 快照")}</div>");
        sb.Append($"</div></section></div><div class='note'>统计时区 {E(data.TimeZone)} · 快照覆盖 {data.Points.Count}/8 个边界 · 新增 BP 为本周发生、期末仍在前 100 的成绩<br>统计基线 {data.Start:MM-dd} → {data.End:MM-dd} · BP 期末观测：{(data.BestObservedAt.HasValue ? E(data.BestObservedAt.Value.ToString("u")) : "—")}</div>");
        return await RenderAsync("weekly", sb.ToString(), ct);
    }

    private static string Chart(WeeklyReport report)
    {
        var points = report.Points.Where(x => x.Pp.HasValue && double.IsFinite(x.Pp.Value)).ToArray();
        if (points.Length == 0) return "<div class='empty'>暂无 PP 趋势数据</div>";
        var min = points.Min(x => x.Pp!.Value); var max = points.Max(x => x.Pp!.Value);
        if (max - min < 1) { min -= .5; max += .5; }
        var sb = new StringBuilder("<svg viewBox='0 0 500 190' width='100%' height='190' role='img' aria-label='每日 PP 曲线'><g font-size='14' fill='#747c8a' font-family='Harmony'>");
        for (var i = 0; i < 3; i++)
        {
            var y = 22 + i * 58;
            sb.Append($"<path d='M65 {y}H485' stroke='#dce0e7'/><text x='0' y='{y + 5}'>{N(max - (max - min) * i / 2, "N0")}</text>");
        }
        double X(UserOsuInfoHistory p) => 70 + (p.Date.DayNumber - report.Start.DayNumber) * 410d / 7;
        double Y(UserOsuInfoHistory p) => 22 + (max - p.Pp!.Value) / (max - min) * 116;
        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        UserOsuInfoHistory? previous = null;
        foreach (var p in points)
        {
            if (previous != null && p.Date.DayNumber == previous.Date.DayNumber + 1)
                sb.Append($"<path d='M{F(X(previous))} {F(Y(previous))}L{F(X(p))} {F(Y(p))}' fill='none' stroke='#0066ed' stroke-width='3'/>");
            sb.Append($"<circle cx='{F(X(p))}' cy='{F(Y(p))}' r='4' fill='#0066ed'/>");
            previous = p;
        }
        sb.Append($"<text x='65' y='178'>{report.Start:MM/dd}</text><text x='235' y='178'>{report.Start.AddDays(3):MM/dd}</text><text x='435' y='178'>{report.End:MM/dd}</text></g></svg>");
        return sb.ToString();
    }

    public async Task<byte[]> ComparisonAsync(ScoreComparison data, CancellationToken ct)
    {
        var x = data.Left.Score; var y = data.Right.Score;
        var set = x.Beatmapset ?? data.Map.Beatmapset;
        var cover = await BackgroundAsync(x, ct);
        var sb = new StringBuilder(Header("SCORE COMPARISON", cover,
            $"<div class='song'><div class='eyebrow'>{E(set?.Artist)} / {ModeName(data.GameMode)}</div><h1>{E(set?.Title)} <span class='muted'>/ {E(data.Map.Version)}</span></h1><div class='muted'>mapped by {E(set?.Creator)} · 同一谱面 · ID {data.Map.Id}</div></div>"));
        string Subtitle(ComparedScore c) => $"{c.Score.EndedAt:yyyy-MM-dd HH:mm} UTC · {(c.Source == "local" ? "本地采集" : "官网最佳")}";
        sb.Append($"<div class='compare-head'>{Identity(data.Left.User, await AvatarAsync(data.Left.User, ct), Subtitle(data.Left))}<div class='versus'>同图成绩对比<br><span class='tag'>{(data.Comparable ? "相同游玩条件" : "游玩条件不同")}</span></div>{Identity(data.Right.User, await AvatarAsync(data.Right.User, ct), Subtitle(data.Right), true)}</div><table class='comparison'>");
        void Row(string label, string left, string right, double? a = null, double? b = null, bool lowerBetter = false, bool major = false)
        {
            var highlight = data.Comparable && a.HasValue && b.HasValue && a != b;
            var leftBetter = lowerBetter ? a < b : a > b;
            sb.Append($"<tr{(major ? " class='major'" : "")}><td{(highlight && leftBetter ? " class='accent'" : "")}>{left}</td><td>{label}</td><td{(highlight && !leftBetter ? " style='color:#347990'" : "")}>{right}</td></tr>");
        }
        var delta = data.Comparable && x.Pp.HasValue && y.Pp.HasValue ? $"<br><span class='accent'>左侧 {N(x.Pp - y.Pp, "+0.00;-0.00;0.00")}</span>" : "";
        Row("PP" + delta, N(x.Pp) + " <small>pp</small>", N(y.Pp) + " <small>pp</small>", x.Pp, y.Pp, major: true);
        Row("准确率" + (data.Comparable ? $"<br>{N((x.Accuracy - y.Accuracy) * 100, "+0.00;-0.00;0.00")} 百分点" : ""), N(x.Accuracy * 100) + "%", N(y.Accuracy * 100) + "%", x.Accuracy, y.Accuracy);
        Row("最大连击", N(x.MaxCombo, "N0") + "×", N(y.MaxCombo, "N0") + "×", x.MaxCombo, y.MaxCombo);
        Row("Miss", N(x.Statistics?.Miss, "N0"), N(y.Statistics?.Miss, "N0"), x.Statistics?.Miss, y.Statistics?.Miss, true);
        Row("评级", E(x.Rank.ToString()), E(y.Rank.ToString()));
        var counts = data.GameMode switch
        {
            1 => new[] { ("良 / 可", "great", "ok") },
            2 => new[] { ("果实 / 大水滴", "great", "large_tick_hit"), ("小水滴 / 小水滴 Miss", "small_tick_hit", "small_tick_miss") },
            3 => new[] { ("MAX / 300", "perfect", "great"), ("200 / 100", "good", "ok"), ("50", "meh", "") },
            _ => new[] { ("300 / 100", "great", "ok"), ("50", "meh", "") }
        };
        static int? Count(Statistics? s, string key) => key switch
        { "perfect" => s?.Perfect, "great" => s?.Great, "good" => s?.Good, "ok" => s?.Ok, "meh" => s?.Meh,
            "large_tick_hit" => s?.LargeTickHit, "small_tick_hit" => s?.SmallTickHit, "small_tick_miss" => s?.SmallTickMiss, _ => null };
        foreach (var (label, one, two) in counts)
            Row(label, N(Count(x.Statistics, one), "N0") + (two.Length > 0 ? " / " + N(Count(x.Statistics, two), "N0") : ""),
                N(Count(y.Statistics, one), "N0") + (two.Length > 0 ? " / " + N(Count(y.Statistics, two), "N0") : ""));
        Row("总分（API）", N(x.TotalScore, "N0"), N(y.TotalScore, "N0"), x.TotalScore, y.TotalScore);
        Row("Mods", E(Mods(x)), E(Mods(y)));
        Row("客户端 / Build", E(PlayerReportService.IsLegacy(x) ? "Stable" : "Lazer / " + (x.BuildId?.ToString() ?? "未知")),
            E(PlayerReportService.IsLegacy(y) ? "Stable" : "Lazer / " + (y.BuildId?.ToString() ?? "未知")));
        sb.Append($"</table><div class='note'>{(data.Comparable ? "高亮表示该项数值更优，不代表玩家整体实力" : "Mods 设置、客户端或 Build 不一致，已停用优胜高亮与差值结论")}<br>官网结果为各 Mod 当前最佳；本地结果仅覆盖已采集尝试。缺失字段以 — 表示。</div>");
        return await RenderAsync("comparison", sb.ToString(), ct);
    }

    private async Task<byte[]> RenderAsync(string name, string content, CancellationToken ct)
    {
        var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Rendering", "PlayerFeatures", "templates", name + ".html"), ct);
        var fonts = new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts") + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
        var html = Regex.Replace(template, @"\{\{(fonts|content)\}\}", m => m.Groups[1].Value == "fonts" ? fonts : content);
        return await RenderAttribution.RenderAsync(renderer, html, 1200, 0, ct);
    }
}
