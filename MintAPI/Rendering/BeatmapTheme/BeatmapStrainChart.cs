using System.Text;
using System.Globalization;
using MintAPI.rosu_pp;
using MintAPI.Services;

namespace MintAPI.Rendering.BeatmapTheme;

internal static class BeatmapStrainChart
{
    public static string Render(OsuStrainTimeline timeline, BeatmapStrainSeries[] series, BeatmapBpmSegment[] bpmSegments)
    {
        var points = timeline.Points.Select((point, i) => new
        {
            point.EndTimeMs,
            Values = series.Select(s => s.Values[i]).ToArray(),
            Peak = point.Aim * point.Aim + point.Speed * point.Speed
        }).Where(p => double.IsFinite(p.EndTimeMs) && p.Values.All(double.IsFinite)).ToArray();
        if (points.Length == 0) return "<div class=\"muted\" style=\"font-size:26px;padding-top:64px\">暂无局部难度数据</div>";
        const double left = 64, right = 1378, top = 34, bottom = 196;
        var start = Math.Min(0, points[0].EndTimeMs);
        var end = Math.Max(start + 1, points[^1].EndTimeMs);
        var max = points.Max(p => p.Values.Max());
        if (max <= 0) max = 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max / 4)));
        var step = Math.Ceiling(max / 4 / magnitude) * magnitude;
        var ceiling = Math.Ceiling(max / step) * step;
        var decimals = Math.Max(0, -(int)Math.Floor(Math.Log10(step)));
        var tickFormat = decimals > 8 ? "0.##E+0" : "0" + (decimals > 0 ? "." + new string('#', decimals) : "");
        double X(double time) => left + (right - left) * (time - start) / (end - start);
        double Y(double value) => bottom - (bottom - top) * value / ceiling;
        var hasBpmChanges = bpmSegments.Length > 1;
        var chartHeight = hasBpmChanges ? 272 : 238;
        var timeLabelY = hasBpmChanges ? 265 : 231;
        var svg = new StringBuilder($"<svg role=\"img\" aria-label=\"局部 strain 曲线，共享纵轴\" width=\"1404\" height=\"{chartHeight}\" viewBox=\"0 0 1404 {chartHeight}\">");
        for (var val = 0d; val <= ceiling + step / 2; val += step)
        {
            svg.Append(FormattableString.Invariant($"<line x1=\"{left}\" y1=\"{Y(val):0.##}\" x2=\"{right}\" y2=\"{Y(val):0.##}\" stroke=\"#e5e5ea\"/><text x=\"{left - 12}\" y=\"{Y(val) + 7:0.##}\" text-anchor=\"end\" font-size=\"21\" fill=\"#6e6e73\">{val.ToString(tickFormat, CultureInfo.InvariantCulture)}</text>"));
        }
        // Keep timestamp labels apart on both short and long maps.
        var tickSeconds = Math.Max(1, Math.Ceiling(end / 1000 / 6 / 10) * 10);
        for (var time = 0d; time <= end / 1000; time += tickSeconds)
            svg.Append(FormattableString.Invariant($"<text x=\"{X(time * 1000):0.##}\" y=\"{timeLabelY}\" text-anchor=\"middle\" font-size=\"23\" fill=\"#6e6e73\">{(int)time / 60}:{(int)time % 60:00}</text>"));

        if (hasBpmChanges)
        {
            svg.Append("<g class=\"bpm-band\" aria-label=\"BPM 变化\"><text x=\"52\" y=\"235\" text-anchor=\"end\" font-size=\"17\" fill=\"#6e6e73\">BPM</text>");
            foreach (var segment in bpmSegments)
            {
                var x = X(Math.Max(start, segment.StartTimeMs));
                var width = X(Math.Min(end, segment.EndTimeMs)) - x;
                if (width <= 0) continue;
                var label = segment.Bpm.ToString("0.##", CultureInfo.InvariantCulture);
                svg.Append(FormattableString.Invariant($"<rect x=\"{x:0.##}\" y=\"215\" width=\"{width:0.##}\" height=\"26\" fill=\"#ededf0\"/><line x1=\"{x:0.##}\" x2=\"{x:0.##}\" y1=\"215\" y2=\"241\" stroke=\"#8e8e93\"/>"));
                if (width >= label.Length * 12 + 16)
                    svg.Append(FormattableString.Invariant($"<text x=\"{x + width / 2:0.##}\" y=\"235\" text-anchor=\"middle\" font-size=\"19\" fill=\"#53615c\">{label}</text>"));
            }
            svg.Append("</g>");
        }

        // Bound SVG size while preserving the local maximum of each skill per horizontal pixel.
        for (var i = series.Length - 1; i >= 0; i--)
        {
            var skill = i;
            var plotted = points.GroupBy(p => (int)X(p.EndTimeMs))
                .Select(g => g.MaxBy(p => p.Values[skill])!).OrderBy(p => p.EndTimeMs)
                .Select(p => (X: X(p.EndTimeMs), Y: Y(p.Values[skill]))).ToArray();
            svg.Append($"<path class=\"strain-curve\" fill=\"none\" stroke=\"{series[i].Color}\" stroke-width=\"2.7\" stroke-linecap=\"round\" d=\"");
            svg.Append(SmoothPath(plotted, end - start, right - left));
            svg.Append("\"/>");
        }
        var peaks = new List<int>();
        foreach (var index in Enumerable.Range(0, points.Length).OrderByDescending(i => points[i].Peak))
        {
            var point = points[index];
            if (point.Peak <= 0) break;
            if (peaks.All(p => Math.Abs(points[p].EndTimeMs - point.EndTimeMs) >= 15000 &&
                Math.Abs(X(points[p].EndTimeMs) - X(point.EndTimeMs)) >= 120)) peaks.Add(index);
            if (peaks.Count == 3) break;
        }
        foreach (var index in peaks.OrderBy(p => points[p].EndTimeMs))
        {
            var peak = points[index];
            var x = X(peak.EndTimeMs);
            var anchor = x < 130 ? "start" : x > right - 90 ? "end" : "middle";
            var seconds = peak.EndTimeMs / 1000;
            var label = FormattableString.Invariant($"{(int)seconds / 60}:{seconds % 60:00.0}");
            svg.Append(FormattableString.Invariant($"<line x1=\"{x:0.##}\" y1=\"23\" x2=\"{x:0.##}\" y2=\"{bottom}\" stroke=\"#a1a1a8\" stroke-dasharray=\"5 5\"/><circle cx=\"{x:0.##}\" cy=\"{Y(peak.Values.Max()):0.##}\" r=\"5\" fill=\"#0066cc\"/><text x=\"{x:0.##}\" y=\"20\" text-anchor=\"{anchor}\" font-size=\"23\" font-weight=\"700\" fill=\"#1d1d1f\">{label}</text>"));
        }
        return svg.Append("</svg>").ToString();
    }
    // Monotone cubic Hermite interpolation: keep every plotted sample and prevent
    // control points from creating artificial peaks between neighbouring samples.
    private static string SmoothPath((double X, double Y)[] points, double durationMs, double plotWidth)
    {
        if (points.Length == 0) return "";
        var path = new StringBuilder(FormattableString.Invariant($"M {points[0].X:0.####} {points[0].Y:0.####}"));
        if (points.Length == 1) return path.ToString();
        // Short maps get finer temporal subdivision; long maps use fewer segments
        // once they occupy about two output pixels. Subdivision preserves the same
        // cubic geometry, rather than averaging away short-lived peaks.
        var intervalMs = Math.Max(100, durationMs / plotWidth * 2);
        var slopes = new double[points.Length - 1];
        var tangents = new double[points.Length];
        for (var i = 0; i < slopes.Length; i++)
            slopes[i] = (points[i + 1].Y - points[i].Y) / (points[i + 1].X - points[i].X);
        tangents[0] = slopes[0];
        tangents[^1] = slopes[^1];
        for (var i = 1; i < points.Length - 1; i++)
        {
            if (slopes[i - 1] == 0 || slopes[i] == 0 || Math.Sign(slopes[i - 1]) != Math.Sign(slopes[i])) continue;
            var previousWidth = points[i].X - points[i - 1].X;
            var nextWidth = points[i + 1].X - points[i].X;
            var w1 = 2 * nextWidth + previousWidth;
            var w2 = nextWidth + 2 * previousWidth;
            tangents[i] = (w1 + w2) / (w1 / slopes[i - 1] + w2 / slopes[i]);
        }
        for (var i = 0; i < slopes.Length; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var thirdWidth = (to.X - from.X) / 3;
            var lower = Math.Min(from.Y, to.Y);
            var upper = Math.Max(from.Y, to.Y);
            var c1 = Math.Clamp(from.Y + thirdWidth * tangents[i], lower, upper);
            var c2 = Math.Clamp(to.Y - thirdWidth * tangents[i + 1], lower, upper);
            var intervalDuration = (to.X - from.X) / plotWidth * durationMs;
            var subdivisions = Math.Clamp((int)Math.Ceiling(intervalDuration / intervalMs), 1, 8);
            double Value(double t)
            {
                var u = 1 - t;
                return u * u * u * from.Y + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * to.Y;
            }
            double Derivative(double t)
            {
                var u = 1 - t;
                return 3 * u * u * (c1 - from.Y) + 6 * u * t * (c2 - c1) + 3 * t * t * (to.Y - c2);
            }
            for (var j = 0; j < subdivisions; j++)
            {
                var t0 = (double)j / subdivisions;
                var t1 = (double)(j + 1) / subdivisions;
                var dt = t1 - t0;
                var y0 = Value(t0);
                var y1 = Value(t1);
                var control1 = y0 + Derivative(t0) * dt / 3;
                var control2 = y1 - Derivative(t1) * dt / 3;
                var x0 = from.X + (to.X - from.X) * t0;
                var x1 = from.X + (to.X - from.X) * t1;
                path.Append(FormattableString.Invariant($" C {x0 + (x1 - x0) / 3:0.####} {control1:0.####} {x1 - (x1 - x0) / 3:0.####} {control2:0.####} {x1:0.####} {y1:0.####}"));
            }
        }
        return path.ToString();
    }

}
