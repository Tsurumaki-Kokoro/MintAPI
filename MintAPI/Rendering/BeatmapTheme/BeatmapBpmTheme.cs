using System.Globalization;
using System.Net;
using MintAPI.Services;
using MintOsuApi.Models;
using Scriban;
using Scriban.Runtime;

namespace MintAPI.Rendering.BeatmapTheme;

public sealed class BeatmapBpmTheme(IRenderService renderer)
{
    public async Task<byte[]> RenderAsync(Beatmap map, IReadOnlyList<BeatmapBpmSegment> segments,
        bool includeDetails = false, CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0) throw new ArgumentException("BPM segments are required.", nameof(segments));
        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        static string Time(double milliseconds)
        {
            var value = (long)Math.Round(milliseconds);
            return FormattableString.Invariant($"{value / 60000}:{value / 1000 % 60:00}.{value % 1000:000}");
        }
        var values = new ScriptObject
        {
            ["base_url"] = new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot") + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/'),
            ["title"] = WebUtility.HtmlEncode(map.Beatmapset?.Title ?? $"Beatmap {map.Id}"),
            ["artist"] = WebUtility.HtmlEncode(map.Beatmapset?.Artist ?? ""),
            ["version"] = WebUtility.HtmlEncode(map.Version), ["map_id"] = map.Id,
            ["segments"] = segments.Count, ["changes"] = segments.Count - 1,
            ["bpm_min"] = Number(segments.Min(s => s.Bpm)), ["bpm_max"] = Number(segments.Max(s => s.Bpm)),
            ["constant"] = segments.Count == 1,
            ["include_details"] = includeDetails,
            ["curve_svg"] = BeatmapBpmChart.Render(segments, 0, segments[^1].EndTimeMs, 1888, 420, false),
            ["zoom_charts"] = BeatmapBpmChart.ZoomWindows(segments).Select(window => new
            {
                title = $"{Time(window.Start)}–{Time(window.End)}",
                svg = BeatmapBpmChart.Render(segments, window.Start, window.End, 924, 300, true)
            }).ToArray(),
            ["rows"] = segments.Select((segment, index) => new
            {
                index = index + 1, start = Time(segment.StartTimeMs), end = Time(segment.EndTimeMs),
                duration = ((segment.EndTimeMs - segment.StartTimeMs) / 1000).ToString("0.000", CultureInfo.InvariantCulture),
                bpm = Number(segment.Bpm), previous = index == 0 ? "—" : Number(segments[index - 1].Bpm),
                delta = index == 0 ? "初始" : (segment.Bpm > segments[index - 1].Bpm ? "+" : "") + Number(segment.Bpm - segments[index - 1].Bpm),
                direction = index == 0 ? "initial" : segment.Bpm > segments[index - 1].Bpm ? "up" : "down"
            }).ToArray()
        };
        var path = Path.Combine(AppContext.BaseDirectory, "Rendering", "BeatmapTheme", "templates", "default", "bpm.html");
        var template = Template.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (template.HasErrors) throw new InvalidOperationException(string.Join("\n", template.Messages));
        var context = new TemplateContext();
        context.PushGlobal(values);
        return await RenderAttribution.RenderAsync(renderer, await template.RenderAsync(context), 2000, 0, cancellationToken);
    }
}
