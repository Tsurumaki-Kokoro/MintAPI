using HitCircleAPI.Services;
using Ossapi.Models;
using Scriban;
using Scriban.Runtime;

namespace HitCircleAPI.Rendering.BeatmapTheme;

public class DefaultBeatmapTheme
{
    private static readonly string BeatmapTemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Rendering", "BeatmapTheme", "templates", "default", "beatmap.html");

    private static readonly string BeatmapsetTemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Rendering", "BeatmapTheme", "templates", "default", "beatmapset.html");

    private readonly IRenderService _renderer;
    private readonly IImageCacheService _imageCache;
    private readonly IPpCalculatorService _ppCalc;
    private readonly ILogger<DefaultBeatmapTheme> _logger;

    public DefaultBeatmapTheme(IRenderService renderer, IImageCacheService imageCache,
        IPpCalculatorService ppCalc, ILogger<DefaultBeatmapTheme> logger)
    {
        _renderer = renderer;
        _imageCache = imageCache;
        _ppCalc = ppCalc;
        _logger = logger;
    }

    public async Task<byte[]> RenderBeatmapAsync(Beatmap beatmap, User mapper, byte[] mapBg, string osuFilePath)
    {
        var beatmapset = beatmap.Beatmapset as Beatmapset ?? null;
        var beatmapsetCompact = beatmap.Beatmapset;

        var ppResult = _ppCalc.CalculateSs(osuFilePath, beatmap.ModeInt);        var stars = ppResult.Stars;

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
        var diffBars = diffValues.Select(v => new
        {
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
            rankedDate = "谱面状态非上架";

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
        scriptObj["version"] = TruncateString(beatmap.Version, 60);
        scriptObj["title"] = titleLine;
        scriptObj["artist"] = artistUnicode;
        scriptObj["source"] = beatmapsetCompact?.Source ?? "";
        scriptObj["status"] = beatmap.Status.ToString();
        scriptObj["diff_info"] = diffInfo;
        scriptObj["diff_bars"] = diffBars;
        scriptObj["star_bar_width"] = starBarWidth;
        scriptObj["mapper_avatar_data_url"] = mapperAvatarDataUrl;
        scriptObj["mapper_username"] = mapper.Username;
        scriptObj["ranked_date"] = rankedDate;
        scriptObj["max_combo"] = beatmap.MaxCombo ?? 0;
        scriptObj["ss_pp"] = (int)Math.Round(ppResult.Pp);

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(BeatmapTemplatePath);
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, 1200, 600);
    }

    public async Task<byte[]> RenderBeatmapsetAsync(Beatmapset beatmapset, byte[] coverBg)
    {
        var maps = beatmapset.Beatmaps?
            .OrderBy(m => m.DifficultyRating)
            .ToList() ?? [];

        var displayMaps = maps.Take(20).ToList();
        var extraCount = maps.Count > 20 ? maps.Count - 20 : 0;

        var imgHeight = 400 + 102 * Math.Min(maps.Count > 1 ? maps.Count - 1 : 0, 20);

        var hitLength = beatmapset.Beatmaps?.FirstOrDefault()?.HitLength ?? 0;
        var hitLengthStr = $"{hitLength / 60}:{hitLength % 60:00}";

        string rankedDate;
        if (beatmapset.RankedDate != null)
            rankedDate = beatmapset.RankedDate.Value.ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm:ss");
        else
            rankedDate = "谱面状态可能非ranked";

        var bgDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(coverBg)}";

        var mapItems = displayMaps.Select(m =>
        {
            var modeIcon = m.ModeInt switch { 1 => "t", 2 => "f", 3 => "m", _ => "o" };
            var starsColor = GetStarsColor(m.DifficultyRating);
            var starsTextClass = m.DifficultyRating >= 6.5 ? "gold" : "black";
            var diffBars = new[]
            {
                new { label_name = "CS", width = (int)Math.Min(200, 200 * Math.Min(m.Cs, 10) / 10), label = $"{m.Cs:0.0}" },
                new { label_name = "HP", width = (int)Math.Min(200, 200 * Math.Min(m.Drain, 10) / 10), label = $"{m.Drain:0.0}" },
                new { label_name = "OD", width = (int)Math.Min(200, 200 * Math.Min(m.Accuracy, 10) / 10), label = $"{m.Accuracy:0.0}" },
                new { label_name = "AR", width = (int)Math.Min(200, 200 * Math.Min(m.Ar, 10) / 10), label = $"{m.Ar:0.0}" }
            };
            return new
            {
                mode_icon = modeIcon,
                stars = m.DifficultyRating,
                stars_bg_color = starsColor,
                stars_text_class = starsTextClass,
                version = TruncateString(m.Version, 40),
                map_id = m.Id,
                max_combo = m.MaxCombo ?? 0,
                diff_bars = diffBars
            };
        }).ToArray();

        var scriptObj = new ScriptObject();
        scriptObj["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}";
        scriptObj["bg_data_url"] = bgDataUrl;
        scriptObj["img_height"] = imgHeight;
        scriptObj["title"] = beatmapset.Title;
        scriptObj["artist"] = beatmapset.Artist;
        scriptObj["creator"] = beatmapset.Creator;
        scriptObj["ranked_date"] = rankedDate;
        scriptObj["source"] = beatmapset.Source;
        scriptObj["beatmapset_id"] = beatmapset.Id;
        scriptObj["bpm"] = $"{beatmapset.Bpm:0}";
        scriptObj["hit_length"] = hitLengthStr;
        scriptObj["maps"] = mapItems;
        scriptObj["extra_count"] = extraCount;

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(BeatmapsetTemplatePath);
        var template = Template.Parse(templateSrc);
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, 1200, imgHeight);
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
