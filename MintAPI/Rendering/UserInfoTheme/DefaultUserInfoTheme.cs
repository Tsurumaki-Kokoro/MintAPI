using System.Net;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintOsuApi.Models;
using Scriban;
using Scriban.Runtime;

namespace MintAPI.Rendering.UserInfoTheme;

public class DefaultUserInfoTheme
{
    private static string GetTemplatePath(string theme) => Path.Combine(
        AppContext.BaseDirectory, "Rendering", "UserInfoTheme", "templates", theme, "index.html");

    private readonly IRenderService _renderer;
    private readonly IImageCacheService _imageCache;
    private readonly ILogger<DefaultUserInfoTheme> _logger;

    public DefaultUserInfoTheme(IRenderService renderer, IImageCacheService imageCache,
        ILogger<DefaultUserInfoTheme> logger)
    {
        _renderer = renderer;
        _imageCache = imageCache;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(User user, UserOsuInfoHistory? history, string gameMode, string theme = "default", DateOnly? comparisonToday = null)
    {
        if (theme is not ("default" or "yaowan"))
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported user info theme");

        var stats = user.Statistics;

        // ── Avatar ──────────────────────────────────────────────────
        var avatarBytes = await _imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        var avatarDataUrl = $"data:image/png;base64,{Convert.ToBase64String(avatarBytes)}";

        // ── Background ──────────────────────────────────────────────
        var bannerUrl = !string.IsNullOrWhiteSpace(user.Cover?.Url) ? user.Cover.Url : user.Cover?.CustomUrl;
        var bgBytes = theme == "default" ? await _imageCache.GetUserBannerAsync(bannerUrl, user.Id) : null;
        if (bgBytes is not { Length: > 0 })
            bgBytes = await _imageCache.GetUserBackgroundAsync(user.Id);
        var bgDataUrl = bgBytes is { Length: > 0 }
            ? $"data:{ImageMimeType(bgBytes)};base64,{Convert.ToBase64String(bgBytes)}"
            : "";

        // ── Badges ──────────────────────────────────────────────────
        var badgeCount = user.Badges?.Count ?? 0;
        var badges = new List<object>();
        if (user.Badges != null)
        {
            for (int i = 0; i < user.Badges.Count; i++)
            {
                var badge = user.Badges[i];
                byte[] badgeBytes;
                try
                {
                    badgeBytes = await _imageCache.GetBadgeAsync(badge.Image2xUrl.Length > 0 ? badge.Image2xUrl : badge.ImageUrl, user.Id, i);
                }
                catch
                {
                    continue;
                }
                var dataUrl = $"data:image/png;base64,{Convert.ToBase64String(badgeBytes)}";

                int bx, by;
                if (badgeCount <= 9)
                {
                    bx = 50 + 100 * i;
                    by = 510;
                }
                else if (i < 9)
                {
                    bx = 50 + 100 * i;
                    by = 486;
                }
                else
                {
                    bx = 50 + 100 * (i - 9);
                    by = 534;
                }

                badges.Add(new { x = bx, y = by, data_url = dataUrl, desc = WebUtility.HtmlEncode(badge.Description) });
            }
        }

        // ── Delta (history comparison) ────────────────────────────
        var (globalRankDelta, globalRankDeltaClass) = CalcRankDelta(
            stats?.GlobalRank, history?.GlobalRank);
        var (ppDelta, ppDeltaClass) = CalcPpDelta(
            stats?.Pp, history?.Pp);

        // ── Level & exp bar ──────────────────────────────────────
        var level = stats?.Level.Current ?? 0;
        var levelProgress = stats?.Level.Progress ?? 0;
        var expBarWidth = levelProgress > 0 ? levelProgress * 7 - 3 : 0;

        // ── Grade counts ─────────────────────────────────────────
        var grades = stats?.GradeCounts;

        // ── Formatted values ─────────────────────────────────────
        var globalRank = stats?.GlobalRank is > 0
            ? $"#{stats.GlobalRank:N0}"
            : "#0";

        var countryRankText = BuildCountryRankText(stats?.CountryRank, history?.CountryRank);

        var pp = $"{stats?.Pp:N0}";

        var rankedScore = $"{stats?.RankedScore:N0}";
        var accuracy = BuildAccuracyText(stats?.HitAccuracy, history?.Accuracy);
        var playCount = BuildCountText(stats?.PlayCount, history?.PlayCount);
        var totalScore = $"{stats?.TotalScore:N0}";
        var totalHits = BuildCountText(stats?.TotalHits, history?.TotalHits);
        var playTime = FormatPlayTime(stats?.PlayTime ?? 0);
        // ── Bottom timestamp / history text ──────────────────────
        string timestampText;
        string historyText;
        int timestampX;

        var now = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        var today = comparisonToday ?? DateOnly.FromDateTime(DateTime.Today);
        if (history != null && history.Date <= today)
        {
            var days = today.DayNumber - history.Date.DayNumber;
            timestampX = 260;
            timestampText = now;
            historyText = days == 0 ? "| 数据对比于当日凌晨" : $"| 数据对比于 {days} 天前";
        }
        else
        {
            timestampX = 380;
            timestampText = now;
            historyText = "";
        }

        // ── Build script object ──────────────────────────────────
        var scriptObj = new ScriptObject
        {
            ["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}",
            ["bg_data_url"] = bgDataUrl,
            ["game_mode"] = gameMode,
            ["avatar_data_url"] = avatarDataUrl,
            ["username"] = WebUtility.HtmlEncode(user.Username),
            ["country_code"] = user.CountryCode,
            ["is_supporter"] = user.IsSupporter,
            ["badges"] = badges,
            ["country_rank_text"] = countryRankText,
            ["level"] = level,
            ["level_progress"] = levelProgress,
            ["exp_bar_width"] = expBarWidth,
            ["global_rank"] = globalRank,
            ["global_rank_delta"] = globalRankDelta,
            ["global_rank_delta_class"] = globalRankDeltaClass,
            ["pp"] = pp,
            ["pp_delta"] = ppDelta,
            ["pp_delta_class"] = ppDeltaClass,
            ["grade_ssh"] = grades?.Ssh ?? 0,
            ["grade_ss"] = grades?.Ss ?? 0,
            ["grade_sh"] = grades?.Sh ?? 0,
            ["grade_s"] = grades?.S ?? 0,
            ["grade_a"] = grades?.A ?? 0,
            ["ranked_score"] = rankedScore,
            ["accuracy"] = accuracy,
            ["play_count"] = playCount,
            ["total_score"] = totalScore,
            ["total_hits"] = totalHits,
            ["play_time"] = playTime,
            ["timestamp_x"] = timestampX,
            ["timestamp_text"] = timestampText,
            ["history_text"] = historyText
        };

        if (theme == "default")
        {
            scriptObj["has_stats"] = stats is not null;
            scriptObj["global_rank"] = stats?.GlobalRank is > 0 ? $"#{stats.GlobalRank:N0}" : "—";
            scriptObj["pp"] = stats?.Pp is double currentPp ? $"{currentPp:N0}" : "—";
            scriptObj["ranked_score"] = stats is not null ? $"{stats.RankedScore:N0}" : "—";
            scriptObj["total_score"] = stats is not null ? $"{stats.TotalScore:N0}" : "—";
            scriptObj["level"] = stats is not null ? $"{level}" : "—";
            scriptObj["play_time"] = stats is not null ? playTime : "—";
            if (avatarBytes.Length == 0)
                scriptObj["avatar_data_url"] = AssetDataUrl(Path.Combine("osu-web", "public", "images", "layout", "avatar-guest.png"));
            scriptObj["history_text"] = historyText.TrimStart('|', ' ');
            foreach (var key in new[] { "grade_ssh", "grade_ss", "grade_sh", "grade_s", "grade_a" })
                scriptObj[key] = grades is not null ? $"{(int)scriptObj[key]:N0}" : "—";
            scriptObj["country_rank"] = stats?.CountryRank is > 0 ? $"#{stats.CountryRank:N0}" : "—";
            scriptObj["pp_size"] = scriptObj["pp"].ToString()!.Length <= 7 ? 74 : 54;
            scriptObj["global_rank_size"] = scriptObj["global_rank"].ToString()!.Length <= 8 ? 58 : 46;
            scriptObj["country_rank_size"] = scriptObj["country_rank"].ToString()!.Length <= 7 ? 58 : 46;
            scriptObj["grade_size"] = new[] { "grade_ssh", "grade_ss", "grade_sh", "grade_s", "grade_a" }
                .Max(key => scriptObj[key].ToString()!.Length) <= 6 ? 34 : 24;
            var (countryDelta, countryDeltaClass) = CalcRankDelta(stats?.CountryRank, history?.CountryRank);
            scriptObj["country_rank_delta"] = countryDelta;
            scriptObj["country_rank_delta_class"] = countryDeltaClass;
            scriptObj["accuracy_value"] = stats is not null ? $"{stats.HitAccuracy:0.00}%" : "—";
            scriptObj["accuracy_delta"] = ComparisonSuffix(accuracy);
            scriptObj["play_count_value"] = stats is not null ? $"{stats.PlayCount:N0}" : "—";
            scriptObj["play_count_delta"] = ComparisonSuffix(playCount);
            scriptObj["total_hits_value"] = stats is not null ? $"{stats.TotalHits:N0}" : "—";
            scriptObj["total_hits_delta"] = ComparisonSuffix(totalHits);
            var time = TimeSpan.FromSeconds(stats?.PlayTime ?? 0);
            scriptObj["play_time_short"] = stats is not null ? $"{(int)time.TotalHours:N0}" : "—";
            scriptObj["country_code"] = WebUtility.HtmlEncode(user.CountryCode);
            scriptObj["game_mode"] = WebUtility.HtmlEncode(gameMode);
            foreach (var name in new[] { "world", "trophy", "bolt", "target", "clock", "music", "list-details", "star", "heart" })
                scriptObj["icon_" + name.Replace('-', '_')] = AssetDataUrl(Path.Combine("score", "default", "icons", name + ".svg"));
            var flag = user.CountryCode.Length == 2 && user.CountryCode.All(char.IsAsciiLetter)
                ? AssetDataUrl(Path.Combine("flags", user.CountryCode.ToUpperInvariant() + ".png")) : "";
            scriptObj["country_flag"] = flag;
        }

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(GetTemplatePath(theme));
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return theme == "default"
            ? await RenderAttribution.RenderAsync(_renderer, html, 1500, 0)
            : await _renderer.RenderHtmlAsync(html, 1000, 1350);
    }

    private static string ComparisonSuffix(string value) => value.Contains('(') ? value[(value.IndexOf('(') + 1)..^1] : "";

    private static string ImageMimeType(byte[] data) => data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8
        ? "image/jpeg" : data.Length >= 4 && data[0] == (byte)'R' && data[1] == (byte)'I'
            ? "image/webp" : "image/png";

    private static string AssetDataUrl(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", relativePath);
        if (!File.Exists(path)) return "";
        var type = Path.GetExtension(path) == ".svg" ? "image/svg+xml" : "image/png";
        return $"data:{type};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    // ── Delta helpers ────────────────────────────────────────────────

    private static (string Delta, string CssClass) CalcRankDelta(int? current, int? prev)
    {
        if (current == null || prev == null || current == 0 || prev == 0)
            return ("", "");
        var diff = current.Value - prev.Value;
        if (diff == 0) return ("", "");
        // rank improved: number decreased → ↑ green
        return diff < 0
            ? ($"↑{(-diff):N0}", "delta-up")
            : ($"↓{diff:N0}", "delta-down");
    }

    private static (string Delta, string CssClass) CalcPpDelta(double? current, double? prev)
    {
        if (current == null || prev == null)
            return ("", "");
        var diff = current.Value - prev.Value;
        if (Math.Abs(diff) < 0.01) return ("", "");
        return diff > 0
            ? ($"↑{(int)diff}", "delta-up")
            : ($"↓{(int)(-diff)}", "delta-down");
    }

    private static string BuildCountryRankText(int? current, int? prev)
    {
        if (current == null || current == 0) return "#0";
        if (prev == null || prev == 0) return $"#{current:N0}";
        var diff = current.Value - prev.Value;
        if (diff == 0) return $"#{current:N0}";
        var op = diff < 0 ? "↑" : "↓";
        return $"#{current:N0}({op}{Math.Abs(diff):N0})";
    }

    private static string BuildAccuracyText(double? current, double? prev)
    {
        if (current == null) return "0.00%";
        var acc = current.Value;
        if (prev == null) return $"{acc:0.00}%";
        var diff = acc - prev.Value;
        if (Math.Abs(diff) < 0.005) return $"{acc:0.00}%";
        var op = diff > 0 ? "+" : "-";
        return $"{acc:0.00}%({op}{Math.Abs(diff):0.00}%)";
    }

    private static string BuildCountText(long? current, long? prev)
    {
        if (current == null) return "0";
        if (prev == null) return $"{current:N0}";
        var diff = current.Value - prev.Value;
        if (diff == 0) return $"{current:N0}";
        var op = diff > 0 ? "+" : "-";
        return $"{current:N0}({op}{Math.Abs(diff):N0})";
    }

    private static string FormatPlayTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
    }
}
