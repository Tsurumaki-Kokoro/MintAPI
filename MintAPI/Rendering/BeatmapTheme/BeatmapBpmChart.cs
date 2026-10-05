using System.Globalization;
using System.Text;
using MintAPI.Services;

namespace MintAPI.Rendering.BeatmapTheme;

internal static class BeatmapBpmChart
{
    public static (double Start, double End)[] ZoomWindows(IReadOnlyList<BeatmapBpmSegment> segments)
    {
        var windows = new List<(double Start, double End)>();
        var end = segments[^1].EndTimeMs;
        for (var i = 1; i < segments.Count; i++)
        {
            if (1804 * (segments[i].StartTimeMs - segments[i - 1].StartTimeMs) / end >= 100) continue;
            var first = i - 1;
            while (i + 1 < segments.Count && 1804 * (segments[i + 1].StartTimeMs - segments[i].StartTimeMs) / end < 100) i++;
            // Adjacent windows share a boundary to keep the time sequence continuous.
            var startIndex = first;
            while (startIndex < i)
            {
                var lastIndex = startIndex + 1;
                var minGap = segments[lastIndex].StartTimeMs - segments[startIndex].StartTimeMs;
                var maxGap = minGap;
                while (lastIndex < i && lastIndex - startIndex < 7)
                {
                    var gap = segments[lastIndex + 1].StartTimeMs - segments[lastIndex].StartTimeMs;
                    // Split abrupt time-scale changes so subsecond steps stay readable.
                    if (Math.Max(maxGap, gap) / Math.Min(minGap, gap) > 8) break;
                    minGap = Math.Min(minGap, gap);
                    maxGap = Math.Max(maxGap, gap);
                    lastIndex++;
                }
                var start = segments[startIndex].StartTimeMs;
                var last = segments[lastIndex].StartTimeMs;
                var padding = Math.Max(20, (last - start) * .06);
                windows.Add((Math.Max(0, start - padding), Math.Min(end, last + padding)));
                startIndex = lastIndex;
            }
        }
        return windows.ToArray();
    }

    public static string Render(IReadOnlyList<BeatmapBpmSegment> segments, double start, double end,
        int width, int height, bool detail)
    {
        var visible = segments.Where(s => s.EndTimeMs > start && s.StartTimeMs < end).ToArray();
        if (visible.Length == 0 || end <= start) return "";
        const double left = 64, top = 28;
        var right = width - 20d;
        var bottom = height - 54d;
        var min = visible.Min(s => s.Bpm);
        var max = visible.Max(s => s.Bpm);
        var span = Math.Max(20, max - min);
        var rawStep = span / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = Math.Ceiling(rawStep / magnitude) * magnitude;
        var floor = Math.Max(0, Math.Floor((min - step * .3) / step) * step);
        var ceiling = Math.Ceiling((max + step * .3) / step) * step;
        double X(double time) => left + (right - left) * (time - start) / (end - start);
        double Y(double bpm) => bottom - (bottom - top) * (bpm - floor) / (ceiling - floor);
        var svg = new StringBuilder(FormattableString.Invariant($"<svg class=\"bpm-curve\" role=\"img\" aria-label=\"BPM 时间阶梯曲线\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><text x=\"{left}\" y=\"20\" font-size=\"18\" fill=\"#6e6e73\">BPM</text>"));
        for (var bpm = floor; bpm <= ceiling + step / 2; bpm += step)
            svg.Append(FormattableString.Invariant($"<line x1=\"{left}\" x2=\"{right}\" y1=\"{Y(bpm):0.###}\" y2=\"{Y(bpm):0.###}\" stroke=\"#e5e5ea\"/><text x=\"{left - 10}\" y=\"{Y(bpm) + 6:0.###}\" text-anchor=\"end\" font-size=\"18\" fill=\"#6e6e73\">{bpm:0.##}</text>"));
        for (var tick = 0; tick <= 6; tick++)
        {
            var time = start + (end - start) * tick / 6;
            var milliseconds = (long)Math.Round(time);
            var label = detail
                ? FormattableString.Invariant($"{milliseconds / 60000}:{milliseconds / 1000 % 60:00}.{milliseconds % 1000:000}")
                : FormattableString.Invariant($"{milliseconds / 60000}:{milliseconds / 1000 % 60:00}");
            svg.Append(FormattableString.Invariant($"<line x1=\"{X(time):0.###}\" x2=\"{X(time):0.###}\" y1=\"{top}\" y2=\"{bottom}\" stroke=\"#ededf0\"/><text x=\"{X(time):0.###}\" y=\"{bottom + 32}\" text-anchor=\"{(tick == 0 ? "start" : tick == 6 ? "end" : "middle")}\" font-size=\"18\" fill=\"#6e6e73\">{label}</text>"));
        }
        svg.Append("<path class=\"bpm-step\" fill=\"none\" stroke=\"#0066cc\" stroke-width=\"2.5\" stroke-linejoin=\"round\" d=\"");
        for (var i = 0; i < visible.Length; i++)
        {
            var segment = visible[i];
            if (i == 0)
                svg.Append(FormattableString.Invariant($"M {X(Math.Max(start, segment.StartTimeMs)):0.###} {Y(segment.Bpm):0.###} "));
            else
                svg.Append(FormattableString.Invariant($"V {Y(segment.Bpm):0.###} "));
            svg.Append(FormattableString.Invariant($"H {X(Math.Min(end, segment.EndTimeMs)):0.###} "));
        }
        svg.Append("\"/>");
        var lastLabelRight = double.NegativeInfinity;
        foreach (var segment in visible)
        {
            if (segment.StartTimeMs < start) continue;
            var x = X(segment.StartTimeMs);
            var y = Y(segment.Bpm);
            svg.Append(FormattableString.Invariant($"<circle class=\"bpm-change\" data-time=\"{segment.StartTimeMs:0.###}\" data-bpm=\"{segment.Bpm:0.######}\" cx=\"{x:0.###}\" cy=\"{y:0.###}\" r=\"3\" fill=\"#0066cc\"/>"));
            if (!detail) continue;
            var label = segment.Bpm.ToString("0.##", CultureInfo.InvariantCulture);
            var labelWidth = label.Length * 11;
            var labelX = Math.Clamp(x - labelWidth / 2, left, right - labelWidth);
            if (labelX < lastLabelRight + 12) continue;
            svg.Append(FormattableString.Invariant($"<text class=\"bpm-value\" x=\"{labelX:0.###}\" y=\"{Math.Max(top + 18, y - 10):0.###}\" font-size=\"19\" font-weight=\"600\" fill=\"#1d1d1f\">{label}</text>"));
            lastLabelRight = labelX + labelWidth;
        }
        return svg.Append("</svg>").ToString();
    }
}
