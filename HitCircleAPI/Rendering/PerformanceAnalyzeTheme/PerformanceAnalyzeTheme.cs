using System.Globalization;
using System.Net;
using HitCircleAPI.Services;
using Ossapi.Models;
using Scriban;
using Scriban.Runtime;

namespace HitCircleAPI.Rendering.PerformanceAnalyzeTheme;

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
        var stars = await ResolveStarsAsync(ordered);
        var report = PerformanceAnalyzeData.Build(ordered, stars);
        var avatar = await imageCache.GetAvatarAsync(user.AvatarUrl, user.Id);
        var ppValues = report.PpValues;
        var maximum = ppValues.Count > 0 ? Math.Ceiling(ppValues.Max() / 50) * 50 : 100;
        var minimum = ppValues.Count > 0 ? Math.Floor(ppValues.Min() / 50) * 50 : 0;
        if (maximum <= minimum) maximum = minimum + 50;
        var points = ppValues.Select((value, index) =>
        {
            var x = 62 + index * 824.0 / Math.Max(1, ppValues.Count - 1);
            var y = 238 - (value - minimum) / (maximum - minimum) * 187;
            return $"{x.ToString("0.0", CultureInfo.InvariantCulture)},{y.ToString("0.0", CultureInfo.InvariantCulture)}";
        });

        var values = new ScriptObject
        {
            ["base_url"] = $"file://{Path.Combine(AppContext.BaseDirectory, "wwwroot")}",
            ["avatar_data_url"] = $"data:image/png;base64,{Convert.ToBase64String(avatar)}",
            ["username"] = WebUtility.HtmlEncode(user.Username),
            ["user_id"] = user.Id,
            ["mode"] = WebUtility.HtmlEncode(mode),
            ["weighted_pp"] = report.WeightedPp.ToString("N1"),
            ["raw_pp"] = report.RawPp.ToString("N1"),
            ["bp_count"] = report.Count,
            ["average_accuracy"] = report.HasAccuracy ? $"{report.AverageAccuracy:0.00}%" : "—",
            ["average_stars"] = report.HasStars ? $"{report.AverageStars:0.00}★" : "—",
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
            ["pp_mid_rank"] = Math.Max(1, (ppValues.Count + 1) / 2),
            ["top_ten_width"] = (ppValues.Count <= 10 ? 824 : 824.0 * 9 / (ppValues.Count - 1))
                .ToString("0.0", CultureInfo.InvariantCulture),
            ["grades"] = report.Grades.Select(item => new
            {
                label = item.Label, count = item.Count,
                percent = item.Percent.ToString("0.0"), width = item.Percent.ToString("0.###", CultureInfo.InvariantCulture),
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
            ["time_bars"] = MakeBars(report.TimeBars),
            ["accuracy_bars"] = MakeBars(report.AccuracyBars),
            ["bpm_bars"] = MakeBars(report.BpmBars),
            ["scatter"] = report.Scatter.Select(item => new
            {
                x = item.X.ToString("0.###", CultureInfo.InvariantCulture),
                y = item.Y.ToString("0.###", CultureInfo.InvariantCulture),
                color = item.Color
            }).ToArray(),
            ["has_stars"] = report.Scatter.Count > 0,
            ["generated_at"] = DateTime.Now.ToString("yyyy/MM/dd HH:mm")
        };

        var context = new TemplateContext();
        context.PushGlobal(values);
        var template = Template.Parse(await File.ReadAllTextAsync(TemplatePath));
        if (template.HasErrors)
            throw new InvalidOperationException($"Performance analysis template is invalid: {string.Join("; ", template.Messages)}");
        var html = await template.RenderAsync(context);
        return await renderer.RenderHtmlAsync(html, 1600, 1220);
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
        return bars.Select((item, index) => (object)new
        {
            label = WebUtility.HtmlEncode(item.Label), count = item.Count,
            height = item.Height.ToString("0.###", CultureInfo.InvariantCulture),
            highlight = item.Highlight,
            show_label = index == 0 || index == bars.Count - 1 || index % Math.Max(1, bars.Count / 5) == 0
        }).ToArray();
    }
}
