using System.Net;
using HitCircleAPI.Services;
using Ossapi.Models;
using Scriban;
using Scriban.Runtime;

namespace HitCircleAPI.Rendering.BeatmapTheme;

public class DefaultBeatmapTheme
{
    private static string TemplatePath(string theme, string name) => Path.Combine(
        AppContext.BaseDirectory, "Rendering", "BeatmapTheme", "templates", theme, name + ".html");

    private readonly IRenderService _renderer;
    private readonly IImageCacheService _imageCache;
    private readonly IPpCalculatorService _ppCalc;
    private readonly IBeatmapAnalysisService _analysis;
    private readonly ILogger<DefaultBeatmapTheme> _logger;

    public DefaultBeatmapTheme(IRenderService renderer, IImageCacheService imageCache,
        IPpCalculatorService ppCalc, ILogger<DefaultBeatmapTheme> logger, IBeatmapAnalysisService analysis)
    {
        _renderer = renderer;
        _imageCache = imageCache;
        _ppCalc = ppCalc;
        _analysis = analysis;
        _logger = logger;
    }

    public async Task<byte[]> RenderBeatmapAsync(Beatmap beatmap, User mapper, byte[] mapBg, string osuFilePath, string theme = "default")
    {
        ValidateTheme(theme);
        var beatmapset = beatmap.Beatmapset;
        var beatmapsetCompact = beatmap.Beatmapset;

        var analysis = theme == "default" ? _analysis.Calculate(osuFilePath) : null;
        if (analysis is not null && analysis.Mode != beatmap.ModeInt) analysis = null;
        var ppResult = analysis?.Ss ?? _ppCalc.CalculateSs(osuFilePath, beatmap.ModeInt);
        var stars = ppResult.Stars;

        var mapperAvatarBytes = await _imageCache.GetAvatarAsync(mapper.AvatarUrl, mapper.Id);
        var mapperAvatarDataUrl = $"data:image/png;base64,{Convert.ToBase64String(mapperAvatarBytes)}";
        var bgDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(mapBg)}";

        var modeLayout = beatmap.ModeInt switch { 1 => "taiko", 2 => "ctb", 3 => "mania", _ => "std" };
        var modeIcon = beatmap.ModeInt switch { 1 => "t", 2 => "f", 3 => "m", _ => "o" };
        var starsColor = GetStarsColor(stars);
        var starsTextClass = stars >= 6.5 ? "gold" : "black";

        var diffValues = new[]
        {
            beatmap.Cs,
            beatmap.Drain,
            beatmap.Accuracy,
            beatmap.Ar
        };
        var diffBars = diffValues.Select((v, index) => new
        {
            label_name = new[] { "CS", "HP", "OD", "AR" }[index],
            percent = Math.Clamp(v, 0, 10) * 10,
            width = (int)Math.Min(250, 250 * Math.Min(v, 10) / 10),
            label = $"{v:0.0}"
        }).ToArray();

        var starBarWidth = (int)Math.Min(250, 250 * Math.Min(stars, 10) / 10);

        var length = beatmap.TotalLength;
        var diffInfo = new object[]
        {
            $"{length / 60}:{length % 60:00}",
            $"{beatmap.Bpm ?? 0:0}",
            beatmap.CountCircles,
            beatmap.CountSliders
        };

        string rankedDate;
        if (beatmapset?.RankedDate != null)
            rankedDate = beatmapset.RankedDate.Value.ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm:ss");
        else
            rankedDate = "—";

        var scriptObj = new ScriptObject();
        var artistUnicode = beatmapsetCompact?.ArtistUnicode.Length > 0 ? beatmapsetCompact.ArtistUnicode : beatmapsetCompact?.Artist ?? "";
        var titleLine = TruncateString($"{beatmapsetCompact?.Title ?? ""} | by {artistUnicode}", 80);

        scriptObj["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}";
        scriptObj["bg_data_url"] = bgDataUrl;
        scriptObj["mode_layout"] = modeLayout;
        scriptObj["mode_icon"] = modeIcon;
        scriptObj["stars"] = stars;
        scriptObj["stars_bg_color"] = starsColor;
        scriptObj["stars_text_class"] = starsTextClass;
        scriptObj["beatmapset_id"] = beatmap.BeatmapsetId;
        scriptObj["beatmap_id"] = beatmap.Id;
        scriptObj["version"] = WebUtility.HtmlEncode(theme == "default" ? beatmap.Version : TruncateString(beatmap.Version, 60));
        scriptObj["title"] = WebUtility.HtmlEncode(titleLine);
        scriptObj["artist"] = WebUtility.HtmlEncode(artistUnicode);
        scriptObj["source"] = WebUtility.HtmlEncode(beatmapsetCompact?.Source ?? "");
        scriptObj["status"] = beatmap.Status.ToString();
        scriptObj["diff_info"] = diffInfo;
        scriptObj["diff_bars"] = diffBars;
        scriptObj["star_bar_width"] = starBarWidth;
        scriptObj["mapper_avatar_data_url"] = mapperAvatarDataUrl;
        scriptObj["mapper_username"] = WebUtility.HtmlEncode(mapper.Username);
        scriptObj["ranked_date"] = rankedDate;
        scriptObj["max_combo"] = beatmap.MaxCombo ?? 0;
        scriptObj["ss_pp"] = (int)Math.Round(ppResult.Pp);

        if (theme == "default")
        {
            AddAssets(scriptObj);
            scriptObj["map_title"] = WebUtility.HtmlEncode(beatmapsetCompact?.Title ?? "");
            scriptObj["mode_name"] = ModeName(beatmap.ModeInt);
            scriptObj["mode_image"] = ModeImage(beatmap.ModeInt);
            scriptObj["stars_text"] = $"{stars:0.00}";
            scriptObj["star_percent"] = Math.Clamp(stars, 0, 10) * 10;
            scriptObj["combo_text"] = $"{analysis?.Ss.MaxCombo ?? (uint)(beatmap.MaxCombo ?? 0):N0}";
            scriptObj["pp_text"] = analysis is not null ? $"{ppResult.Pp:N2}" : $"{Math.Round(ppResult.Pp):N0}";
            scriptObj["circles_text"] = $"{beatmap.CountCircles:N0}";
            scriptObj["sliders_text"] = $"{beatmap.CountSliders:N0}";
            scriptObj["has_analysis"] = analysis is not null;
            scriptObj["img_height"] = analysis is not null ? 1768 : 900;
            if (analysis is not null) AddAnalysis(scriptObj, analysis);
        }

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(TemplatePath(theme, "beatmap"));
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, theme == "default" ? 1500 : 1200,
            theme == "default" ? analysis is not null ? 1768 : 900 : 600);
    }

    public async Task<byte[]> RenderBeatmapsetAsync(Beatmapset beatmapset, byte[] coverBg, string theme = "default")
    {
        ValidateTheme(theme);
        var maps = beatmapset.Beatmaps?
            .OrderBy(m => m.DifficultyRating)
            .ToList() ?? [];

        var displayMaps = maps.Take(20).ToList();
        var extraCount = maps.Count > 20 ? maps.Count - 20 : 0;

        var imgHeight = 400 + 102 * Math.Min(maps.Count > 1 ? maps.Count - 1 : 0, 20);

        if (theme == "default")
            imgHeight = 602 + 120 * displayMaps.Count + (extraCount > 0 ? 60 : 0);

        var hitLength = beatmapset.Beatmaps?.FirstOrDefault()?.HitLength ?? 0;
        var hitLengthStr = $"{hitLength / 60}:{hitLength % 60:00}";

        string rankedDate;
        if (beatmapset.RankedDate != null)
            rankedDate = beatmapset.RankedDate.Value.ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm:ss");
        else
            rankedDate = "—";

        var bgDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(coverBg)}";

        var mapItems = displayMaps.Select(m =>
        {
            var modeIcon = m.ModeInt switch { 1 => "t", 2 => "f", 3 => "m", _ => "o" };
            var starsColor = GetStarsColor(m.DifficultyRating);
            var starsTextClass = m.DifficultyRating >= 6.5 ? "gold" : "black";
            var diffBars = new[]
            {
                new { label_name = "CS", percent = Math.Clamp(m.Cs, 0, 10) * 10, width = (int)Math.Min(200, 200 * Math.Min(m.Cs, 10) / 10), label = $"{m.Cs:0.0}" },
                new { label_name = "HP", percent = Math.Clamp(m.Drain, 0, 10) * 10, width = (int)Math.Min(200, 200 * Math.Min(m.Drain, 10) / 10), label = $"{m.Drain:0.0}" },
                new { label_name = "OD", percent = Math.Clamp(m.Accuracy, 0, 10) * 10, width = (int)Math.Min(200, 200 * Math.Min(m.Accuracy, 10) / 10), label = $"{m.Accuracy:0.0}" },
                new { label_name = "AR", percent = Math.Clamp(m.Ar, 0, 10) * 10, width = (int)Math.Min(200, 200 * Math.Min(m.Ar, 10) / 10), label = $"{m.Ar:0.0}" }
            };
            return new
            {
                mode_icon = modeIcon,
                mode_name = ModeName(m.ModeInt),
                mode_image = theme == "default" ? ModeImage(m.ModeInt) : "",
                stars_text = $"{m.DifficultyRating:0.00}",
                combo_text = $"{m.MaxCombo ?? 0:N0}",
                stars = m.DifficultyRating,
                stars_bg_color = starsColor,
                stars_text_class = starsTextClass,
                version = WebUtility.HtmlEncode(theme == "default" ? m.Version : TruncateString(m.Version, 40)),
                map_id = m.Id,
                max_combo = m.MaxCombo ?? 0,
                diff_bars = diffBars
            };
        }).ToArray();

        var scriptObj = new ScriptObject();
        scriptObj["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}";
        scriptObj["bg_data_url"] = bgDataUrl;
        scriptObj["img_height"] = imgHeight;
        scriptObj["title"] = WebUtility.HtmlEncode(beatmapset.Title);
        scriptObj["artist"] = WebUtility.HtmlEncode(beatmapset.Artist);
        scriptObj["creator"] = WebUtility.HtmlEncode(beatmapset.Creator);
        scriptObj["ranked_date"] = rankedDate;
        scriptObj["source"] = WebUtility.HtmlEncode(beatmapset.Source);
        scriptObj["beatmapset_id"] = beatmapset.Id;
        scriptObj["bpm"] = $"{beatmapset.Bpm:0}";
        scriptObj["hit_length"] = hitLengthStr;
        scriptObj["maps"] = mapItems;
        scriptObj["extra_count"] = extraCount;
        if (theme == "default") AddAssets(scriptObj);

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(TemplatePath(theme, "beatmapset"));
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, theme == "default" ? 1500 : 1200, imgHeight);
    }

    private static void ValidateTheme(string theme)
    {
        if (theme is not ("default" or "yaowan"))
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unsupported beatmap theme");
    }

    private static string ModeName(int mode) => mode switch
    {
        1 => "osu!taiko", 2 => "osu!catch", 3 => "osu!mania", _ => "osu!"
    };

    private static void AddAssets(ScriptObject values)
    {
        foreach (var name in new[] { "music", "star", "stars", "bolt", "link", "clock", "target", "list-details", "adjustments-horizontal" })
            values["icon_" + name.Replace('-', '_')] = AssetDataUrl(Path.Combine("score", "default", "icons", name + ".svg"));
        foreach (var (key, name) in new[] { ("bpm", "music"), ("total_length", "clock"), ("count_circles", "circle"), ("count_sliders", "link") })
            values["icon_" + key] = AssetDataUrl(Path.Combine("score", "default", "icons", name + ".svg"));
    }

    private static void AddAnalysis(ScriptObject values, BeatmapAnalysis analysis)
    {
        values["analysis_mode"] = analysis.Mode;
        values["has_pp_components"] = analysis.Mode <= 1;
        values["reference_title"] = analysis.Mode switch { 2 => "接果 PP 参考", 3 => "判定 PP 参考", _ => "FC PP" };
        values["reference_context"] = analysis.Mode switch
        {
            2 => "FC / 漏接对比", 3 => "仅 320 / 200 · 0 Miss", _ => $"{analysis.Ss.MaxCombo:N0}× · 0 Miss"
        };
        values["accuracy_references"] = analysis.AccuracyReferences.Select(row => new
        {
            label = analysis.Mode == 2 ? FormattableString.Invariant($"{row.Label} · {row.Accuracy:0.00}%")
                : row.Label ?? FormattableString.Invariant($"{row.Accuracy:0.#}%"),
            accuracy = analysis.Mode == 2 ? FormattableString.Invariant($"{row.Accuracy:0.00}%") : "",
            detail = WebUtility.HtmlEncode(row.Detail ?? ""),
            pp = FormattableString.Invariant($"{row.Pp:0.00}"),
            font_size = analysis.Mode <= 1 ? row.Pp < 1000 ? 41 : row.Pp < 10000 ? 36 : 32
                : row.Pp < 1000 ? 55 : row.Pp < 10000 ? 49 : 43
        }).ToArray();
        values["pp_components"] = analysis.Mode == 1 ? new[]
        {
            new { name = "难度", pp = $"{analysis.DifficultyPp:N2}", color = "#0066cc", icon = values["icon_bolt"] },
            new { name = "准确率", pp = $"{analysis.Ss.AccuracyPp:N2}", color = "#53615c", icon = values["icon_target"] }
        } : new[]
        {
            new { name = "Aim", pp = $"{analysis.Ss.AimPp:N2}", color = "#0066cc", icon = values["icon_target"] },
            new { name = "Speed", pp = $"{analysis.Ss.SpeedPp:N2}", color = "#bf651d", icon = values["icon_bolt"] },
            new { name = "Acc", pp = $"{analysis.Ss.AccuracyPp:N2}", color = "#53615c", icon = values["icon_target"] }
        };
        values["mod_references"] = analysis.Mods.Select(row => new
        {
            name = row.Name, stars = $"{row.Stars:0.00}", pp = $"{row.Pp:N2}", ar = $"{row.Ar:0.00}", od = $"{row.Od:0.00}",
            great_window = $"±{row.GreatHitWindow:0.0}", ok_window = $"±{row.OkHitWindow:0.0}",
            icon = row.Name == "NM" ? "" : AssetDataUrl(Path.Combine("osu-web", "public", "images", "badges", "mods",
                row.Name switch { "HD" => "mod-hidden.svg", "HR" => "mod-hard-rock.svg", "HT" => "mod-half-time.svg",
                    "EZ" => "mod-easy.svg", "NF" => "mod-no-fail.svg", _ => "mod-double-time.svg" }))
        }).ToArray();
        values["curve_legend"] = analysis.Curves.Select(s => new { name = s.Name, color = s.Color }).ToArray();
        values["skills_title"] = analysis.Mode switch { 1 => "四项难度", 2 => "接果构成", 3 => "物件与长条", _ => "Aim / Speed 难度" };
        if (analysis.Skills is { } skills)
        {
            var scale = Math.Max(4, Math.Ceiling(Math.Max(skills.Aim, skills.Speed)));
            values["skill_values"] = new[]
            {
                new { name = "Aim", value = $"{skills.Aim:0.000}", percent = skills.Aim / scale * 100, color = "#0066cc" },
                new { name = "Speed", value = $"{skills.Speed:0.000}", percent = skills.Speed / scale * 100, color = "#bf651d" }
            };
            values["aim_with_sliders"] = $"{skills.Aim:0.0000}";
            values["aim_without_sliders"] = $"{skills.Aim * skills.SliderFactor:0.0000}";
            values["slider_aim_drop"] = $"{(skills.Aim > 0 ? (1 - skills.SliderFactor) * 100 : 0):0.000}";
        }
        else if (analysis.Mode == 1)
        {
            var raw = new[] { analysis.Ruleset.Stamina, analysis.Ruleset.Rhythm, analysis.Ruleset.Color, analysis.Ruleset.Reading };
            var scale = Math.Max(4, Math.Ceiling(raw.Max()));
            values["skill_values"] = raw.Select((value, i) => new
            {
                name = analysis.Curves[i].Name, value = value is > 0 and < .001 ? $"{value:0.000000}" : $"{value:0.000}", percent = value / scale * 100, color = analysis.Curves[i].Color
            }).ToArray();
            values["mono_stamina"] = analysis.Ruleset.MonoStaminaFactor is > 0 and < .001 ? "&lt;0.1%" : $"{analysis.Ruleset.MonoStaminaFactor * 100:0.0}%";
        }
        else
        {
            var counts = analysis.Ruleset;
            var raw = analysis.Mode == 2 ? new[] { counts.Fruits, counts.Droplets, counts.TinyDroplets }
                : new[] { counts.Objects - counts.Holds, counts.Holds };
            var names = analysis.Mode == 2 ? new[] { "水果", "水滴", "小水滴" } : new[] { "短键", "长条 LN" };
            var total = raw.Sum(v => (double)v);
            values["composition_rows"] = raw.Select((value, i) => new
            {
                name = names[i], value = $"{value:N0}", percent = total == 0 ? 0 : value / total * 100,
                color = analysis.Curves[0].Color, icon = values[i == 1 ? "icon_link" : "icon_target"]
            }).ToArray();
            values["composition_metric_label"] = analysis.Mode == 2 ? "预读时间" : "LN 占比";
            values["composition_metric"] = analysis.Mode == 2 ? $"{counts.Preempt:0} ms"
                : $"{(counts.Objects == 0 ? 0 : 100d * counts.Holds / counts.Objects):0.0}%";
        }
        values["strain_svg"] = BeatmapStrainChart.Render(analysis.Strains, analysis.Curves);
    }

    private static string ModeImage(int mode)
    {
        var name = mode switch { 1 => "taiko", 2 => "ctb", 3 => "mania", _ => "std" };
        return AssetDataUrl(Path.Combine("beatmap", "default", "icons", name + ".svg"));
    }

    private static string AssetDataUrl(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", relativePath);
        return File.Exists(path) ? $"data:image/svg+xml;base64,{Convert.ToBase64String(File.ReadAllBytes(path))}" : "";
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

    private static string TruncateString(string s, int max) =>
        s.Length > max ? s[..(max - 3)] + "..." : s;
}
