using System.Text;
using System.Collections.Concurrent;
using MintAPI.Services;
using MintOsuApi.Models;
using Scriban;
using Scriban.Runtime;

namespace MintAPI.Rendering.ScoreTheme;

public class DefaultScoreTheme
{
    private static readonly ConcurrentDictionary<string, string> AssetDataUrls = new();

    private static string AssetDataUrl(string relativePath) => AssetDataUrls.GetOrAdd(relativePath, path =>
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", path);
        if (!File.Exists(fullPath)) return "";
        var mime = Path.GetExtension(path) == ".svg" ? "image/svg+xml" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(fullPath))}";
    });

    private static string FlagDataUrl(string? countryCode)
    {
        var country = countryCode?.ToUpperInvariant() ?? "";
        return country.Length == 2 && country.All(char.IsAsciiLetter)
            ? AssetDataUrl(Path.Combine("flags", country + ".png")) : "";
    }

    private static string ModDataUrl(string name) => name.Length <= 4 && name.All(char.IsAsciiLetterOrDigit)
        ? AssetDataUrl(Path.Combine("score", "default", "mods", name + ".png")) : "";

    private static readonly Lazy<Dictionary<string, string>> IconDataUrls = new(() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "score", "default", "icons"), "*.svg")
            .ToDictionary(path => "icon_" + Path.GetFileNameWithoutExtension(path).Replace('-', '_'),
                path => "data:image/svg+xml;base64," + Convert.ToBase64String(File.ReadAllBytes(path))));

    private static string TemplatePath(string theme) => Path.Combine(
        AppContext.BaseDirectory, "Rendering", "ScoreTheme", "templates", theme, "index.html");

    private readonly IRenderService _renderer;
    private readonly IImageCacheService _imageCache;
    private readonly IPpCalculatorService _ppCalc;
    private readonly ILogger<DefaultScoreTheme> _logger;
    private readonly IBeatmapFileService? _beatmapFiles;

    public DefaultScoreTheme(IRenderService renderer, IImageCacheService imageCache,
        IPpCalculatorService ppCalc, ILogger<DefaultScoreTheme> logger, IBeatmapFileService? beatmapFiles = null)
    {
        _renderer = renderer;
        _imageCache = imageCache;
        _ppCalc = ppCalc;
        _logger = logger;
        _beatmapFiles = beatmapFiles;
    }

    public async Task<byte[]> RenderAsync(Score score, User user, byte[] mapBg,
        string osuFilePath, BeatmapDifficultyAttributes? diffAttrs, string theme = "default", string heading = "成绩")
    {
        if (theme is not ("default" or "yaowan"))
            throw new ArgumentOutOfRangeException(nameof(theme));
        var beatmap = score.Beatmap;
        var beatmapset = score.Beatmapset ?? beatmap?.Beatmapset;
        var mods = score.Mods ?? [];

        var ppResult = _ppCalc.Calculate(score, osuFilePath);
        var (ifPp, ssPp) = _ppCalc.CalculateIfFcAndSs(score, osuFilePath);

        var avatarBytes = await _imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        var avatarDataUrl = avatarBytes.Length > 0
            ? $"data:image/png;base64,{Convert.ToBase64String(avatarBytes)}"
            : AssetDataUrl(Path.Combine("osu-web", "public", "images", "layout", "avatar-guest.png"));
        var bgDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(mapBg)}";

        var baseUrl = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}";

        var modeLayout = score.RulesetId switch { 1 or 5 => "taiko", 2 or 6 => "ctb", 3 => "mania", _ => "std" };
        var modeIcon = score.RulesetId switch { 1 or 5 => "t", 2 or 6 => "f", 3 => "m", _ => "o" };

        var stars = ppResult.Stars;
        var starsColor = GetStarsColor(stars);
        var starsTextClass = stars >= 6.5 ? "gold" : "black";

        var modNames = mods.Where(m => m.Acronym != "CL" && !(m.Acronym == "DT" && mods.Any(n => n.Acronym == "NC"))).Select(m => m.Acronym).ToList();

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
                diffAttrs?.OverallDifficulty ?? ApplyModsToOd(beatmap?.Accuracy ?? 0, mods),
                diffAttrs?.ApproachRate ?? ApplyModsToAr(beatmap?.Ar ?? 0, mods),
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
        // Embedded library icons also work when Chromium loads a temporary file without a web origin.
        if (theme == "default")
            foreach (var (key, dataUrl) in IconDataUrls.Value) scriptObj[key] = dataUrl;
        if (theme == "default")
        {
            scriptObj["country_flag"] = FlagDataUrl(user.CountryCode);
            scriptObj["mod_badges"] = modNames.Select(name => new
            {
                name = System.Net.WebUtility.HtmlEncode(name), image = ModDataUrl(name)
            }).ToArray();
            foreach (var name in new[] { "count_circles", "count_sliders" })
                scriptObj[name + "_image"] = AssetDataUrl(Path.Combine("osu-web", "public", "images", "layout", "beatmapset-page", name + ".svg"));
        }
        scriptObj["heading"] = heading;
        scriptObj["mode_name"] = modeLayout == "std" ? "osu!" : modeLayout == "ctb" ? "osu!catch" : "osu!" + modeLayout;
        scriptObj["accuracy"] = $"{score.Accuracy * 100:0.00}%";
        var showComponents = modeLayout == "std" && ppResult.AimPp.HasValue && ppResult.SpeedPp.HasValue && ppResult.AccuracyPp.HasValue;
        scriptObj["show_components"] = showComponents;
        scriptObj["aim_pp"] = $"{ppResult.AimPp:0.00}";
        scriptObj["speed_pp"] = $"{ppResult.SpeedPp:0.00}";
        scriptObj["accuracy_pp"] = $"{ppResult.AccuracyPp:0.00}";
        // Reserve two character widths for CJK text so long localized titles can wrap without clipping.
        static int TextWidth(string? value) => value?.Sum(c => c > 255 ? 2 : 1) ?? 0;
        var longIdentity = TextWidth(beatmapset?.Title) > 45 || TextWidth(beatmap?.Version) > 45;
        var identityHeight = longIdentity ? 210 : 170;
        identityHeight = Math.Max(identityHeight, 44 + (int)Math.Ceiling(TextWidth(beatmapset?.Title) / 37d) * 46
            + (int)Math.Ceiling(TextWidth(string.IsNullOrEmpty(beatmapset?.ArtistUnicode) ? beatmapset?.Artist : beatmapset.ArtistUnicode) / 48d) * 36
            + (int)Math.Ceiling((TextWidth(beatmap?.Version) + TextWidth(beatmapset?.Creator)
                + modNames.Sum(name => TextWidth(name) + 5) + 20) / 50d) * 38);
        identityHeight = Math.Max(identityHeight, 112 + (int)Math.Ceiling(TextWidth(user.Username) / 18d) * 52);
        scriptObj["identity_height"] = identityHeight;
        scriptObj["title_size"] = longIdentity ? 42 : 58;
        scriptObj["image_height"] = theme == "default" ? identityHeight + 1070 : 720;
        var ppText = $"{score.Pp ?? ppResult.Pp:0.00}";
        scriptObj["pp"] = ppText;
        scriptObj["pp_size"] = ppText.Length <= 6 ? 124 : ppText.Length == 7 ? 96 : 82;
        scriptObj["fc_pp"] = $"{ifPp:0.00}";
        scriptObj["ss_pp"] = $"{ssPp:0.00}";
        scriptObj["combo"] = $"{score.MaxCombo:N0} / {ppResult.MaxCombo:N0}";
        scriptObj["combo_actual"] = $"{score.MaxCombo:N0}";
        scriptObj["combo_max"] = $"{ppResult.MaxCombo:N0}";
        scriptObj["miss_count"] = score.Statistics?.Miss is int miss ? $"{miss:N0}" : "—";
        scriptObj["has_miss"] = score.Statistics?.Miss > 0;
        scriptObj["mania_ratio"] = modeLayout == "mania" && score.Statistics?.Great > 0 && score.Statistics.Perfect.HasValue
            ? $"{(double)score.Statistics.Perfect.Value / score.Statistics.Great.Value:0.00}" : "—";
        scriptObj["judgements"] = BuildJudgements(score);
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
        scriptObj["artist"] = !string.IsNullOrEmpty(beatmapset?.ArtistUnicode) ? beatmapset.ArtistUnicode : beatmapset?.Artist ?? "";
        scriptObj["version"] = beatmap?.Version ?? "";
        scriptObj["creator"] = beatmapset?.Creator ?? "";
        scriptObj["rank"] = scoreRankName;
        scriptObj["score_formatted"] = scoreFormatted;
        scriptObj["ended_at"] = score.EndedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss");
        scriptObj["rank_global"] = score.RankGlobal?.ToString() ?? "-";
        scriptObj["rank_global_label"] = score.RankGlobal.HasValue ? $"#{score.RankGlobal:N0}" : "—";
        scriptObj["username"] = user.Username;
        scriptObj["country_rank"] = $"{user.Statistics?.CountryRank:N0}";
        scriptObj["country_rank_label"] = user.Statistics?.CountryRank is int countryRank ? $"#{countryRank:N0}" : "—";
        scriptObj["grade_display"] = scoreRankName switch { "X" => "SS", "XH" => "SSH", _ => scoreRankName };
        scriptObj["grade_tone"] = scoreRankName switch { "A" => "green", "B" => "blue", "C" => "purple", "D" or "F" => "red", _ => "gold" };
        scriptObj["stats_html"] = statsHtml;
        scriptObj["avatar_data_url"] = avatarDataUrl;

        var templateCtx = new TemplateContext();
        templateCtx.PushGlobal(scriptObj);

        var templateSrc = await File.ReadAllTextAsync(TemplatePath(theme));
        var template = Template.Parse(templateSrc);
        if (template.HasErrors) throw new InvalidOperationException(template.Messages.ToString());
        // Escape API text before inserting it into HTML; generated SVG/stats remain markup.
        foreach (var key in new[] { "title", "artist", "version", "creator", "username", "heading", "country_code" })
            scriptObj[key] = System.Net.WebUtility.HtmlEncode(scriptObj[key]?.ToString());
        scriptObj["mods"] = modNames.Select(System.Net.WebUtility.HtmlEncode).ToArray();
        var html = await template.RenderAsync(templateCtx);

        return await _renderer.RenderHtmlAsync(html, 1500, (int)scriptObj["image_height"]);
    }

    public Task<byte[]> RenderBestListAsync(List<Score> scores, User user, int firstIndex,
        CancellationToken cancellationToken = default)
        => RenderListAsync(scores, user, firstIndex, recent: false, cancellationToken);

    public Task<byte[]> RenderRecentListAsync(List<Score> scores, User user, int firstIndex,
        CancellationToken cancellationToken = default)
        => RenderListAsync(scores, user, firstIndex, recent: true, cancellationToken);

    public Task<byte[]> RenderFixAsync(BpFixReport report, User user, CancellationToken cancellationToken = default)
        => RenderListAsync(report.Entries.Select(e => e.Score).ToList(), user, 1, false, cancellationToken, report);

    public Task<byte[]> RenderNewBestListAsync(NewBestPlayReport report, User user, CancellationToken cancellationToken = default)
        => RenderListAsync(report.Entries.Select(entry => entry.Score).ToList(), user, report.First, false,
            cancellationToken, newBest: report);

    private async Task<byte[]> RenderListAsync(List<Score> scores, User user, int firstIndex, bool recent,
        CancellationToken cancellationToken, BpFixReport? fix = null, NewBestPlayReport? newBest = null)
    {
        if (scores.Count is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(scores));
        cancellationToken.ThrowIfCancellationRequested();
        static string Escape(string? text) => System.Net.WebUtility.HtmlEncode(text ?? "");
        static int TextWidth(string? text) => text?.Sum(c => c > 255 ? 2 : 1) ?? 0;
        var avatar = await _imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        static int SetId(Score score)
        {
            var id = (score.Beatmapset ?? score.Beatmap?.Beatmapset)?.Id ?? 0;
            return id > 0 ? id : score.Beatmap?.BeatmapsetId ?? 0;
        }
        var covers = new Dictionary<int, string>();
        if (_beatmapFiles is not null)
        {
            using var concurrency = new SemaphoreSlim(4);
            var images = await Task.WhenAll(scores.Select(SetId).Where(id => id > 0).Distinct().Select(async id =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    var bytes = await _beatmapFiles.GetListCoverAsync(id, cancellationToken);
                    var mime = bytes is { Length: > 4 } && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg";
                    return (id, url: bytes is { Length: > 0 } ? $"data:{mime};base64,{Convert.ToBase64String(bytes)}" : "");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to load list cover for set {SetId}", id);
                    return (id, url: "");
                }
                finally { concurrency.Release(); }
            }));
            covers = images.ToDictionary(image => image.id, image => image.url);
        }
        var rows = scores.Select((score, index) =>
        {
            var map = score.Beatmap;
            var set = score.Beatmapset ?? map?.Beatmapset;
            var modNames = (score.Mods ?? []).Where(m => m.Acronym != "CL" &&
                !(m.Acronym == "DT" && score.Mods!.Any(n => n.Acronym == "NC"))).Select(m => m.Acronym).ToList();
            if (modNames.Count == 0) modNames.Add("NM");
            var mods = modNames.Select(name => new
            {
                name = Escape(name),
                image = ModDataUrl(name)
            }).ToArray();
            var rank = score.Rank.ToString().ToUpperInvariant();
            var artist = string.IsNullOrEmpty(set?.ArtistUnicode) ? set?.Artist : set.ArtistUnicode;
            var height = Math.Max(200, 60 + (int)Math.Ceiling(TextWidth(set?.Title) / 29d) * 48
                + (int)Math.Ceiling((TextWidth(map?.Version) + modNames.Sum(name => TextWidth(name) + 5) + 6) / 43d) * 34
                + (int)Math.Ceiling(TextWidth(artist) / 42d) * 30);
            return new
            {
                position = fix?.Entries[index].OldRank ?? newBest?.Entries[index].BpRank ?? firstIndex + index, height = fix is null && newBest is null ? height : Math.Max(height, 260),
                played_at = newBest is null ? "" : score.EndedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'"),
                fixed_rank = fix?.Entries[index].NewRank ?? 0,
                fixed_pp = fix is null ? "" : $"{fix.Entries[index].Fixed.Pp:0.00}",
                gain = fix is null ? "" : $"+{fix.Entries[index].Fixed.Pp - (score.Pp ?? 0):0.00}",
                combo = fix is null ? "" : $"{score.MaxCombo:N0}x → {fix.Entries[index].Fixed.MaxCombo:N0}x · {score.Statistics?.Miss ?? 0} miss",
                title = Escape(set?.Title), artist = Escape(artist), version = Escape(map?.Version),
                mods,
                background = covers.GetValueOrDefault(SetId(score), ""),
                rank = rank switch { "X" => "SS", "XH" => "SSH", _ => rank },
                grade_tone = rank switch { "A" => "green", "B" => "blue", "C" => "purple", "D" or "F" => "red", _ => "gold" },
                pp = score.Pp.HasValue ? $"{score.Pp:0.00}" : "—",
                pp_size = score.Pp >= 10000 ? 60 : score.Pp >= 1000 ? 72 : 80,
                accuracy = $"{score.Accuracy * 100:0.00}%",
                failed = recent && !score.Passed,
                weight = !recent && score.Weight is not null ? $"{score.Weight.Percentage:0.0}% · {score.Weight.Pp:0.00} pp" : ""
            };
        }).ToArray();
        var headerHeight = Math.Max(132, 64 + (int)Math.Ceiling(TextWidth(user.Username) / 32d) * 52);
        var imageHeight = 112 + headerHeight + rows.Sum(row => row.height) + (fix is null ? 0 : 150);
        var globals = new ScriptObject
        {
            ["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}",
            ["username"] = Escape(user.Username), ["country_code"] = Escape(user.CountryCode),
            ["mode_name"] = string.Join(" / ", scores.Select(score => score.RulesetId switch
                { 1 or 5 => "osu!taiko", 2 or 6 => "osu!catch", 3 => "osu!mania", _ => "osu!" }).Distinct()),
            ["is_supporter"] = user.IsSupporter, ["recent"] = recent,
            ["heading"] = newBest is not null ? "NEW BEST PLAYS" : fix is not null ? "BP FIX" : recent ? "RECENT PLAYS" : "BEST PLAYS",
            ["fix"] = fix is not null, ["new_best"] = newBest is not null,
            ["days"] = newBest?.Days ?? 0, ["matched_total"] = newBest?.Total ?? 0,
            ["current_pp"] = $"{fix?.CurrentPp:0.00}", ["total_fixed_pp"] = $"{fix?.FixedPp:0.00}",
            ["weighted_gain"] = $"{fix?.Gain:0.00}",
            ["candidate_count"] = fix?.CandidateCount ?? 0, ["skipped_count"] = fix?.SkippedCount ?? 0,
            ["first"] = firstIndex, ["last"] = firstIndex + scores.Count - 1,
            ["image_height"] = imageHeight, ["header_height"] = headerHeight,
            ["avatar"] = avatar.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(avatar)}"
                : AssetDataUrl(Path.Combine("osu-web", "public", "images", "layout", "avatar-guest.png")),
            ["rows"] = rows,
            ["background_fallback"] = AssetDataUrl(Path.Combine("osu-web", "public", "images", "icons", "beatmapsets.svg"))
        };
        globals["flag"] = FlagDataUrl(user.CountryCode);
        foreach (var (key, dataUrl) in IconDataUrls.Value) globals[key] = dataUrl;
        var context = new TemplateContext(); context.PushGlobal(globals);
        var path = Path.Combine(AppContext.BaseDirectory, "Rendering", "ScoreTheme", "templates", "default", "list.html");
        var template = Template.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (template.HasErrors) throw new InvalidOperationException(template.Messages.ToString());
        return await _renderer.RenderHtmlAsync(await template.RenderAsync(context), 1500, imageHeight, cancellationToken);
    }

    private static object[] BuildJudgements(Score score)
    {
        var s = score.Statistics ?? new Statistics();
        (string Label, int? Value)[] values = score.RulesetId switch
        {
            1 or 5 => [("300", s.Great), ("100", s.Ok), ("MISS", s.Miss)],
            2 or 6 => [("FRUIT", s.Great), ("DROPLET", s.LargeTickHit), ("TINY", s.SmallTickHit), ("TINY MISS", s.SmallTickMiss), ("MISS", s.Miss)],
            3 => [("MAX", s.Perfect), ("300", s.Great), ("200", s.Good), ("100", s.Ok), ("50", s.Meh), ("MISS", s.Miss)],
            _ => [("300", s.Great), ("100", s.Ok), ("50", s.Meh), ("MISS", s.Miss)]
        };
        return values.Select(v => (object)new
        {
            label = v.Label, value = v.Value.HasValue ? $"{v.Value:N0}" : "—",
            tone = v.Label switch { "MISS" or "TINY MISS" => "red", "100" => "green", "50" => "gold", "MAX" => "purple", "200" => "teal", _ => "blue" }
        }).ToArray();
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

    private static double ClockRate(List<NonLegacyMod> mods)
    {
        var speed = mods.FirstOrDefault(m => m.Acronym is "DT" or "NC" or "HT" or "DC");
        if (speed?.Settings?.TryGetValue("speed_change", out var setting) == true &&
            double.TryParse(Convert.ToString(setting, System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate) && rate > 0)
            return rate;
        return speed?.Acronym is "DT" or "NC" ? 1.5 : speed?.Acronym is "HT" or "DC" ? .75 : 1;
    }

    private static double ApplyModsToAr(double ar, List<NonLegacyMod> mods)
    {
        ar = ApplyModsToHp(ar, mods);
        var window = ar < 5 ? 1800 - 120 * ar : 1200 - 150 * (ar - 5);
        window /= ClockRate(mods);
        return window > 1200 ? (1800 - window) / 120 : 5 + (1200 - window) / 150;
    }

    private static double ApplyModsToOd(double od, List<NonLegacyMod> mods)
        => (80 - (80 - 6 * ApplyModsToHp(od, mods)) / ClockRate(mods)) / 6;

    private static double ApplyModsToBpm(double bpm, List<NonLegacyMod> mods) => bpm * ClockRate(mods);

    private static string ApplyModsToLength(int seconds, List<NonLegacyMod> mods)
    {
        seconds = (int)(seconds / ClockRate(mods));
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
        var maxCombo = diffAttrs?.MaxCombo ?? (int)ppResult.MaxCombo;

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
