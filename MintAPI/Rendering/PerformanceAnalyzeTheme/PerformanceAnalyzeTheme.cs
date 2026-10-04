using System.Globalization;
using System.Net;
using MintAPI.Services;
using MintOsuApi.Models;
using Scriban;
using Scriban.Runtime;

namespace MintAPI.Rendering.PerformanceAnalyzeTheme;

public sealed class PerformanceAnalyzeTheme(
    IRenderService renderer,
    IImageCacheService imageCache,
    IBeatmapFileService beatmapFiles,
    IPpCalculatorService ppCalculator,
    ILogger<PerformanceAnalyzeTheme> logger)
{
    private static readonly string TemplatePath = Path.Combine(
        AppContext.BaseDirectory, "Rendering", "PerformanceAnalyzeTheme", "templates", "default", "index.html");

    private static readonly HashSet<string> StarMods =
        ["DT", "NC", "HT", "HR", "EZ", "DC", "DA"];

    public async Task<byte[]> RenderAsync(User user, IReadOnlyList<Score> scores, string mode)
    {
        var ordered = scores.OrderByDescending(score => score.Pp ?? 0).Take(100).ToArray();
        var starsTask = ResolveStarsAsync(ordered);
        var avatarTask = imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        var topPlaysTask = BuildTopPlaysAsync(ordered.Take(10).ToArray());
        await Task.WhenAll(starsTask, avatarTask, topPlaysTask);
        var stars = await starsTask;
        var report = PerformanceAnalyzeData.Build(ordered, stars);
        var avatar = await avatarTask;
        var ppValues = report.PpValues;
        var maximum = ppValues.Count > 0 ? Math.Ceiling(ppValues.Max() / 50) * 50 : 100;
        var minimum = ppValues.Count > 0 ? Math.Floor(ppValues.Min() / 50) * 50 : 0;
        if (maximum <= minimum) maximum = minimum + 50;
        var points = ppValues.Select((value, index) =>
        {
            var x = 62 + index * 824.0 / Math.Max(1, ppValues.Count - 1);
            var y = 226 - (value - minimum) / (maximum - minimum) * 166;
            return $"{x.ToString("0.0", CultureInfo.InvariantCulture)},{y.ToString("0.0", CultureInfo.InvariantCulture)}";
        });

        var scatterStarMaximum = Math.Max(7, Math.Ceiling(report.MaxStars));
        var scatterStarMinimum = Math.Floor(report.MinStars);
        var scatterStarRange = Math.Max(1, scatterStarMaximum - scatterStarMinimum);
        var values = new ScriptObject
        {
            ["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}",
            ["avatar_data_url"] = avatar.Length > 0 ? $"data:image/png;base64,{Convert.ToBase64String(avatar)}"
                : AssetDataUrl(Path.Combine("osu-web", "public", "images", "layout", "avatar-guest.png")),
            ["username"] = WebUtility.HtmlEncode(user.Username),
            ["user_id"] = user.Id,
            ["country_code"] = WebUtility.HtmlEncode(user.CountryCode),
            ["flag"] = user.CountryCode is { Length: 2 } country && country.All(char.IsAsciiLetter)
                ? AssetDataUrl(Path.Combine("flags", $"{country.ToUpperInvariant()}.png")) : "",
            ["mode"] = WebUtility.HtmlEncode(mode),
            ["weighted_pp"] = report.WeightedPp.ToString("N1"),
            ["pp_font_size"] = report.WeightedPp >= 100000 ? 104 : 128,
            ["average_accuracy"] = report.HasAccuracy ? $"{report.AverageAccuracy:0.00}%" : "—",
            ["average_stars"] = report.HasStars ? $"{report.AverageStars:0.00}" : "—",
            ["average_bpm"] = report.HasBpm ? report.AverageBpm.ToString("0.0") : "—",
            ["average_length"] = report.HasLength ? $"{(int)report.AverageLength / 60}:{(int)report.AverageLength % 60:00}" : "—",
            ["top_mod"] = WebUtility.HtmlEncode(report.TopMod),
            ["top_mapper"] = WebUtility.HtmlEncode(report.TopMapper),
            ["top_ten_share"] = report.TopTenShare.ToString("0.0"),
            ["pp_decay"] = report.PpDecay.ToString("0.0"),
            ["star_range"] = report.MinStars > 0 ? $"{report.MinStars:0.0}–{report.MaxStars:0.0}★" : "暂无",
            ["peak_star_bucket"] = report.PeakStarBucket,
            ["pp_curve"] = string.Join(" ", points),
            ["pp_max"] = maximum.ToString("N0"),
            ["pp_min"] = minimum.ToString("N0"),
            ["pp_first"] = ppValues.Count > 0 ? ppValues[0].ToString("N0") : "0",
            ["pp_last"] = ppValues.Count > 0 ? ppValues[^1].ToString("N0") : "0",
            ["curve_ticks"] = Enumerable.Range(0, Math.Min(5, ppValues.Count)).Select(index =>
            {
                var rank = 1 + (int)Math.Round(index * (ppValues.Count - 1.0) / Math.Max(1, Math.Min(5, ppValues.Count) - 1));
                return new { rank, x = (62 + (rank - 1) * 824.0 / Math.Max(1, ppValues.Count - 1)).ToString("0.0", CultureInfo.InvariantCulture) };
            }).ToArray(),
            ["pp_middle"] = ((maximum + minimum) / 2).ToString("N0"),
            ["top_ten_width"] = (ppValues.Count <= 10 ? 824 : 824.0 * 9 / (ppValues.Count - 1))
                .ToString("0.0", CultureInfo.InvariantCulture),
            ["grades"] = report.Grades.Select(item => new
            {
                label = item.Label, count = item.Count,
                percent = item.Percent.ToString("0.#"), width = item.Percent.ToString("0.###", CultureInfo.InvariantCulture),
                color = item.Color
            }).ToArray(),
            ["mods"] = report.Mods.Select(item => new
            {
                label = WebUtility.HtmlEncode(item.Label), value = item.Value.ToString("N1"),
                width = item.Width.ToString("0.###", CultureInfo.InvariantCulture)
            }).ToArray(),
            ["mappers"] = report.Mappers.Select(item => new
            {
                label = WebUtility.HtmlEncode(item.Label), value = item.Value.ToString("N1"),
                width = item.Width.ToString("0.###", CultureInfo.InvariantCulture)
            }).ToArray(),
            ["combo_bars"] = MakeBars(report.ComboBars),
            ["combo_bucket_size"] = report.ComboBucketSize.ToString("N0"),
            ["accuracy_bars"] = MakeBars(report.AccuracyBars),
            ["bpm_bars"] = MakeBars(report.BpmBars),
            ["scatter"] = report.Scatter.Select(item => new
            {
                x = (62 + (item.X * scatterStarMaximum / 100 - scatterStarMinimum) / scatterStarRange * 444)
                    .ToString("0.###", CultureInfo.InvariantCulture),
                y = (260 - item.Y * 2.2).ToString("0.###", CultureInfo.InvariantCulture),
                color = item.Color
            }).ToArray(),
            ["scatter_star_ticks"] = Enumerable.Range(0, 5).Select(index => new
            {
                label = (scatterStarMinimum + index * scatterStarRange / 4).ToString("0.0"),
                x = (62 + index * 111).ToString(CultureInfo.InvariantCulture)
            }).ToArray(),
            ["scatter_pp_ticks"] = Enumerable.Range(0, 3).Select(index => new
            {
                label = (index * Math.Max(100, Math.Ceiling(ppValues.FirstOrDefault() / 100) * 100) / 2).ToString("N0"),
                y = 260 - index * 110
            }).ToArray(),
            ["top_plays"] = await topPlaysTask,
            ["background_fallback"] = AssetDataUrl(Path.Combine("osu-web", "public", "images", "icons", "beatmapsets.svg")),
            ["has_stars"] = report.Scatter.Count > 0,
            ["generated_at"] = DateTime.Now.ToString("yyyy/MM/dd HH:mm")
        };
        foreach (var name in new[] { "target", "star", "music", "clock", "adjustments-horizontal", "list-details", "link", "trophy" })
            values[$"icon_{name.Replace('-', '_')}"] = AssetDataUrl(Path.Combine("score", "default", "icons", $"{name}.svg"));

        var context = new TemplateContext();
        context.PushGlobal(values);
        var template = Template.Parse(await File.ReadAllTextAsync(TemplatePath));
        if (template.HasErrors)
            throw new InvalidOperationException($"Performance analysis template is invalid: {string.Join("; ", template.Messages)}");
        var html = await template.RenderAsync(context);
        return await renderer.RenderHtmlAsync(html, 2000, 1800);
    }

    private async Task<object[]> BuildTopPlaysAsync(IReadOnlyList<Score> scores)
    {
        static int SetId(Score score)
        {
            var id = (score.Beatmapset ?? score.Beatmap?.Beatmapset)?.Id ?? 0;
            return id > 0 ? id : score.Beatmap?.BeatmapsetId ?? 0;
        }

        using var gate = new SemaphoreSlim(4);
        var images = await Task.WhenAll(scores.Select(SetId).Where(id => id > 0).Distinct().Select(async id =>
        {
            await gate.WaitAsync();
            try
            {
                var bytes = await beatmapFiles.GetListCoverAsync(id);
                var mime = bytes is { Length: > 4 } && bytes[0] == 0x89 && bytes[1] == 0x50 ? "image/png" : "image/jpeg";
                return (id, url: bytes is { Length: > 0 } ? $"data:{mime};base64,{Convert.ToBase64String(bytes)}" : "");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load BP analysis cover for set {SetId}", id);
                return (id, url: "");
            }
            finally { gate.Release(); }
        }));
        var covers = images.ToDictionary(image => image.id, image => image.url);
        return scores.Select((score, index) =>
        {
            var set = score.Beatmapset ?? score.Beatmap?.Beatmapset;
            var mods = (score.Mods ?? []).Select(mod => mod.Acronym)
                .Where(mod => !string.IsNullOrWhiteSpace(mod) && mod != "CL" &&
                    !(mod == "DT" && score.Mods!.Any(item => item.Acronym == "NC"))).Distinct().ToArray();
            var rank = score.Rank.ToString();
            return (object)new
            {
                position = index + 1,
                title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(set?.Title) ? "未知谱面" : set.Title),
                artist = WebUtility.HtmlEncode(set?.Artist ?? ""),
                version = WebUtility.HtmlEncode(score.Beatmap?.Version ?? "—"),
                mods = WebUtility.HtmlEncode(mods.Length > 0 ? string.Join(" · ", mods) : "NM"),
                background = covers.GetValueOrDefault(SetId(score), ""),
                rank,
                grade_color = rank switch { "A" => "#359c3e", "B" => "#0066ed", "C" => "#9868ce", "D" or "F" => "#df2346", _ => "#b47d00" },
                pp = score.Pp is { } pp && double.IsFinite(pp) && pp >= 0 ? pp.ToString("0.00") : "—",
                pp_size = score.Pp >= 100000 ? 22 : score.Pp >= 10000 ? 26 : score.Pp >= 1000 ? 30 : 36,
                accuracy = double.IsFinite(score.Accuracy) && score.Accuracy is >= 0 and <= 1 ? $"{score.Accuracy * 100:0.00}%" : "—"
            };
        }).ToArray();
    }

    private static string AssetDataUrl(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", relativePath);
        if (!File.Exists(path)) return "";
        var mime = Path.GetExtension(path) == ".svg" ? "image/svg+xml" : "image/png";
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private async Task<double[]> ResolveStarsAsync(IReadOnlyList<Score> scores)
    {
        var stars = new double[scores.Count];
        using var gate = new SemaphoreSlim(4);
        await Task.WhenAll(scores.Select(async (score, index) =>
        {
            var fallback = score.Beatmap?.DifficultyRating ?? 0;
            stars[index] = fallback;
            if (score.Beatmap is null || score.Mods?.Any(mod => StarMods.Contains(mod.Acronym)) != true)
                return;

            await gate.WaitAsync();
            try
            {
                var path = await beatmapFiles.GetOsuFilePathAsync(score.Beatmap.BeatmapsetId, score.Beatmap.Id);
                var calculated = ppCalculator.Calculate(score, path).Stars;
                if (double.IsFinite(calculated) && calculated > 0)
                    stars[index] = calculated;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Using unmodified star rating for beatmap {BeatmapId}", score.Beatmap.Id);
            }
            finally
            {
                gate.Release();
            }
        }));
        return stars;
    }

    private static object[] MakeBars(IReadOnlyList<AnalysisBar> bars)
    {
        var labels = Enumerable.Range(0, Math.Min(4, bars.Count))
            .Select(index => (int)Math.Round(index * (bars.Count - 1.0) / Math.Max(1, Math.Min(4, bars.Count) - 1)))
            .ToHashSet();
        return bars.Select((item, index) => (object)new
        {
            label = WebUtility.HtmlEncode(item.Label), count = item.Count,
            height = item.Height.ToString("0.###", CultureInfo.InvariantCulture),
            highlight = item.Highlight,
            show_label = labels.Contains(index)
        }).ToArray();
    }
}
