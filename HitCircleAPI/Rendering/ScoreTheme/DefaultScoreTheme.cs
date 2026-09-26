using System.Text;
using HitCircleAPI.Services;
using Ossapi.Models;
using Scriban;
using Scriban.Runtime;

namespace HitCircleAPI.Rendering.ScoreTheme;

public class DefaultScoreTheme
{
    private static readonly string TemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Rendering", "ScoreTheme", "templates", "default", "index.html");

    private readonly IRenderService _renderer;
    private readonly IImageCacheService _imageCache;
    private readonly IPpCalculatorService _ppCalc;
    private readonly ILogger<DefaultScoreTheme> _logger;

    public DefaultScoreTheme(IRenderService renderer, IImageCacheService imageCache,
        IPpCalculatorService ppCalc, ILogger<DefaultScoreTheme> logger)
    {
        _renderer = renderer;
        _imageCache = imageCache;
        _ppCalc = ppCalc;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(Score score, User user, byte[] mapBg,
        string osuFilePath, BeatmapDifficultyAttributes? diffAttrs)
    {
        var beatmap = score.Beatmap;
        var beatmapset = score.Beatmapset ?? beatmap?.Beatmapset;
        var mods = score.Mods ?? [];

        var ppResult = _ppCalc.Calculate(score, osuFilePath);
        var (ifPp, ssPp) = _ppCalc.CalculateIfFcAndSs(score, osuFilePath);

        var avatarBytes = await _imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        var avatarDataUrl = $"data:image/png;base64,{Convert.ToBase64String(avatarBytes)}";
        var bgDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(mapBg)}";

        var baseUrl = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}";

        var modeLayout = score.RulesetId switch { 1 or 5 => "taiko", 2 or 6 => "ctb", 3 => "mania", _ => "std" };
        var modeIcon = score.RulesetId switch { 1 or 5 => "t", 2 or 6 => "f", 3 => "m", _ => "o" };

        var stars = ppResult.Stars;
        var starsColor = GetStarsColor(stars);
        var starsTextClass = stars >= 6.5 ? "gold" : "black";

        var modNames = mods.Select(m => m.Acronym).ToList();

        bool hasHidden = mods.Any(m => m.Acronym is "HD" or "FL" or "FI");
        var rankingList = hasHidden
            ? new[] { "XH", "SH", "A", "B", "C", "D", "F" }
            : new[] { "X", "S", "A", "B", "C", "D", "F" };
        var scoreRankName = score.Rank.ToString().ToUpper();
        bool rankFound = false;
        var rankList = rankingList.Select(r =>
        {
            double opacity;
            if (rankFound) { opacity = 0.5; }
            else if (r == scoreRankName) { rankFound = true; opacity = 1.0; }
            else { opacity = 0.2; }
            return new { name = r, opacity };
        }).ToArray();

        var rulesetId = score.RulesetId;
        double[] diffValues;
        if (rulesetId is 0 or 4 or 8)
        {
            diffValues =
            [
                ApplyModsToCs(beatmap?.Cs ?? 0, mods),
                ApplyModsToHp(beatmap?.Drain ?? 0, mods),
                diffAttrs?.OverallDifficulty ?? beatmap?.Accuracy ?? 0,
                diffAttrs?.ApproachRate ?? beatmap?.Ar ?? 0,
            ];
        }
        else
        {
            diffValues =
            [
                ApplyModsToCs(beatmap?.Cs ?? 0, mods),
                ApplyModsToHp(beatmap?.Drain ?? 0, mods),
                beatmap?.Accuracy ?? 0,
                beatmap?.Ar ?? 0,
            ];
        }

        var diffBars = diffValues.Select(v => new
        {
            width = (int)Math.Min(250, 250 * Math.Min(v, 10) / 10),
            label = Math.Abs(v - Math.Round(v)) < 0.001 ? $"{v:0}" : $"{v:0.0}"
        }).ToArray();

        var starBarWidth = (int)Math.Min(250, 250 * Math.Min(stars, 10) / 10);

        var length = ApplyModsToLength(beatmap?.TotalLength ?? 0, mods);
        var bpm = ApplyModsToBpm(beatmap?.Bpm ?? 0, mods);
        var diffInfo = new object[]
        {
            length,
            $"{bpm:0}",
            beatmap?.CountCircles ?? 0,
            beatmap?.CountSliders ?? 0
        };

        var scoreValue = score.LegacyTotalScore > 0 ? score.LegacyTotalScore : score.TotalScore;
        var scoreFormatted = $"{scoreValue:N0}";

        var statsHtml = BuildStatsHtml(score, ppResult, ifPp, ssPp, diffAttrs);

        var scriptObj = new ScriptObject();
        scriptObj["base_url"] = baseUrl;
        scriptObj["bg_data_url"] = bgDataUrl;
        scriptObj["mode_layout"] = modeLayout;
        scriptObj["mode_icon"] = modeIcon;
        scriptObj["stars"] = stars;
        scriptObj["stars_bg_color"] = starsColor;
        scriptObj["stars_text_class"] = starsTextClass;
        scriptObj["mods"] = modNames;
        scriptObj["rank_list"] = rankList;
        scriptObj["acc_svg"] = BuildAccSvg(score.Accuracy, rulesetId);
        scriptObj["country_code"] = user.CountryCode;
        scriptObj["is_supporter"] = user.IsSupporter;
        scriptObj["diff_bars"] = diffBars;
        scriptObj["star_bar_width"] = starBarWidth;
        scriptObj["diff_info"] = diffInfo;
        scriptObj["beatmap_status"] = beatmap?.Status.ToString() ?? "";
        scriptObj["beatmap_id"] = beatmap?.Id ?? 0;
        scriptObj["title"] = beatmapset?.Title ?? "";
        scriptObj["artist"] = beatmapset?.ArtistUnicode.Length > 0 ? beatmapset.ArtistUnicode : beatmapset?.Artist ?? "";
        scriptObj["version"] = beatmap?.Version ?? "";
        scriptObj["creator"] = beatmapset?.Creator ?? "";
        scriptObj["rank"] = scoreRankName;
        scriptObj["score_formatted"] = scoreFormatted;
        scriptObj["ended_at"] = score.EndedAt.ToString("yyyy-MM-dd HH:mm:ss");
        scriptObj["rank_global"] = score.RankGlobal?.ToString() ?? "-";
        scriptObj["username"] = user.Username;
        scriptObj["country_rank"] = $"{user.Statistics?.CountryRank:N0}";
        scriptObj["stats_html"] = statsHtml;
        scriptObj["avatar_data_url"] = avatarDataUrl;

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(TemplatePath);
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, 1500, 720);
    }

    private static string GetStarsColor(double stars)
    {
        if (stars < 0.1) return "rgb(170,170,170)";
        if (stars >= 9) return "rgb(0,0,0)";
        var colors = new[]
        {
            (77, 177, 254), (59, 152, 254), (46, 101, 254), (101, 99, 241),
            (162, 66, 201), (223, 54, 127), (225, 52, 77), (235, 87, 37),
            (255, 187, 0)
        };
        int idx = Math.Min((int)stars, colors.Length - 1);
        var (r, g, b) = colors[idx];
        return $"rgb({r},{g},{b})";
    }

    private static double ApplyModsToCs(double cs, List<NonLegacyMod> mods)
    {
        if (mods.Any(m => m.Acronym == "HR")) return Math.Min(cs * 1.3, 10);
        if (mods.Any(m => m.Acronym == "EZ")) return Math.Min(cs * 0.5, 10);
        return cs;
    }

    private static double ApplyModsToHp(double hp, List<NonLegacyMod> mods)
    {
        if (mods.Any(m => m.Acronym == "HR")) return Math.Min(hp * 1.4, 10);
        if (mods.Any(m => m.Acronym == "EZ")) return Math.Min(hp * 0.5, 10);
        return hp;
    }

    private static double ApplyModsToBpm(double bpm, List<NonLegacyMod> mods)
    {
        if (mods.Any(m => m.Acronym == "DT" || m.Acronym == "NC")) return bpm * 1.5;
        if (mods.Any(m => m.Acronym == "HT")) return bpm * 0.75;
        return bpm;
    }

    private static string ApplyModsToLength(int seconds, List<NonLegacyMod> mods)
    {
        if (mods.Any(m => m.Acronym == "DT" || m.Acronym == "NC")) seconds = (int)(seconds / 1.5);
        else if (mods.Any(m => m.Acronym == "HT")) seconds = (int)(seconds / 0.75);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string BuildAccSvg(double accuracy, int rulesetId)
    {
        var acc = accuracy * 100;
        var notAcc = 100 - acc;

        double[] inSize = rulesetId switch
        {
            0 or 4 or 8 => [60, 20, 7, 7, 5, 1],
            1 or 5 => [60, 20, 5, 5, 4, 1],
            2 or 6 => [85, 5, 4, 4, 1, 1],
            _ => [70, 10, 10, 5, 4, 1]
        };
        var inColors = new[] { "#ff5858", "#ea7948", "#d99d03", "#72c904", "#0096a2", "#be0089" };

        var sb = new StringBuilder();
        sb.AppendLine("<svg width='340' height='290' viewBox='-1 -1 2 2' style='overflow:visible'>");

        AppendDonutSlice(sb, acc, notAcc, 1.0, 0.80, "#66cbfd", "#66cbfd33");
        double inTotal = inSize.Sum();
        double startAngle = 0;
        for (int i = 0; i < inSize.Length; i++)
        {
            AppendRingSlice(sb, startAngle, startAngle + (inSize[i] / inTotal) * 360, 0.8, 0.75, inColors[i]);
            startAngle += inSize[i] / inTotal * 360;
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void AppendDonutSlice(StringBuilder sb, double mainPct, double restPct,
        double outerR, double innerR, string mainColor, string restColor)
    {
        double mainAngle = mainPct / 100 * 360;
        AppendRingSlice(sb, -90, -90 + mainAngle, outerR, innerR, mainColor);
        AppendRingSlice(sb, -90 + mainAngle, 270, outerR, innerR, restColor);
    }

    private static void AppendRingSlice(StringBuilder sb, double startDeg, double endDeg,
        double outerR, double innerR, string color)
    {
        if (Math.Abs(endDeg - startDeg) < 0.001) return;

        double startRad = startDeg * Math.PI / 180;
        double endRad = endDeg * Math.PI / 180;

        bool largeArc = endDeg - startDeg > 180;

        double x1O = outerR * Math.Cos(startRad);
        double y1O = outerR * Math.Sin(startRad);
        double x2O = outerR * Math.Cos(endRad);
        double y2O = outerR * Math.Sin(endRad);
        double x1I = innerR * Math.Cos(endRad);
        double y1I = innerR * Math.Sin(endRad);
        double x2I = innerR * Math.Cos(startRad);
        double y2I = innerR * Math.Sin(startRad);

        var arc = largeArc ? "1" : "0";
        sb.Append($"<path d='M {F(x1O)} {F(y1O)} A {F(outerR)} {F(outerR)} 0 {arc} 1 {F(x2O)} {F(y2O)} ");
        sb.Append($"L {F(x1I)} {F(y1I)} A {F(innerR)} {F(innerR)} 0 {arc} 0 {F(x2I)} {F(y2I)} Z' ");
        sb.AppendLine($"fill='{color}'/>");
    }

    private static string F(double v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

    private static string BuildStatsHtml(Score score, PpResult ppResult,
        double ifPp, double ssPp, BeatmapDifficultyAttributes? diffAttrs)
    {
        var sb = new StringBuilder();
        var rulesetId = score.RulesetId;
        var stats = score.Statistics;
        var maxCombo = diffAttrs?.MaxCombo ?? 0;

        static string Span(double x, double y, string cls, string text, string? extra = null) =>
            $"<span class='abs {cls}' style='left:{x}px;top:{y}px;transform:translate(-50%,-50%){(extra != null ? ";" + extra : "")}'>{System.Net.WebUtility.HtmlEncode(text)}</span>\n";

        if (rulesetId is 0 or 4 or 8)
        {
            sb.Append(Span(720, 550, "f30", $"{ssPp:0}"));
            sb.Append(Span(840, 550, "f30", $"{ifPp:0}"));
            sb.Append(Span(960, 550, "f30", $"{ppResult.Pp:0}"));
            sb.Append(Span(1157, 550, "f30", $"{score.Accuracy * 100:0.00}%"));
            sb.Append(Span(1385, 550, "f30", $"{score.MaxCombo:N0}/{maxCombo:N0}"));
            sb.Append(Span(1100, 645, "f30", $"{stats?.Great ?? 0}"));
            sb.Append(Span(1214, 645, "f30", $"{stats?.Ok ?? 0}"));
            sb.Append(Span(1328, 645, "f30", $"{stats?.Meh ?? 0}"));
            sb.Append(Span(1442, 645, "f30", $"{stats?.Miss ?? 0}"));
        }
        else if (rulesetId is 1 or 5)
        {
            sb.Append(Span(1118, 550, "f30", $"{score.Accuracy * 100:0.00}%"));
            sb.Append(Span(1270, 550, "f30", $"{score.MaxCombo:N0}"));
            sb.Append(Span(1420, 550, "f30", $"{ppResult.Pp:0}/{ssPp:0}"));
            sb.Append(Span(1118, 645, "f30", $"{stats?.Great ?? 0}"));
            sb.Append(Span(1270, 645, "f30", $"{stats?.Ok ?? 0}"));
            sb.Append(Span(1420, 645, "f30", $"{stats?.Miss ?? 0}"));
        }
        else if (rulesetId is 2 or 6)
        {
            sb.Append(Span(1083, 550, "f30", $"{score.Accuracy * 100:0.00}%"));
            sb.Append(Span(1247, 550, "f30", $"{score.MaxCombo:N0}/{maxCombo:N0}"));
            sb.Append(Span(1411, 550, "f30", $"{ppResult.Pp:0}/{ssPp:0}"));
            sb.Append(Span(1062, 645, "f30", $"{stats?.Great ?? 0}"));
            sb.Append(Span(1185, 645, "f30", $"{stats?.LargeTickHit ?? 0}"));
            sb.Append(Span(1309, 645, "f30", $"{stats?.SmallTickMiss ?? 0}"));
            sb.Append(Span(1432, 645, "f30", $"{stats?.Miss ?? 0}"));
        }
        else
        {
            var ratioStr = (stats?.Great ?? 0) != 0
                ? $"{(double)(stats?.Perfect ?? 0) / (stats?.Great ?? 1):0.0}:1"
                : "∞:1";
            sb.Append(Span(1002, 580, "f20", ratioStr));
            sb.Append(Span(1002, 550, "f30", $"{score.Accuracy * 100:0.00}%"));
            sb.Append(Span(1197, 550, "f30", $"{score.MaxCombo:N0}"));
            sb.Append(Span(1395, 550, "f30", $"{ppResult.Pp:0}/{ssPp:0}"));
            sb.Append(Span(953, 645, "f30", $"{stats?.Perfect ?? 0}"));
            sb.Append(Span(1051, 645, "f30", $"{stats?.Great ?? 0}"));
            sb.Append(Span(1150, 645, "f30", $"{stats?.Good ?? 0}"));
            sb.Append(Span(1249, 645, "f30", $"{stats?.Ok ?? 0}"));
            sb.Append(Span(1347, 645, "f30", $"{stats?.Meh ?? 0}"));
            sb.Append(Span(1445, 645, "f30", $"{stats?.Miss ?? 0}"));
        }

        return sb.ToString();
    }
}
