using System.Globalization;

namespace MintAPI.Services;

public record BeatmapBpmSegment(double StartTimeMs, double EndTimeMs, double Bpm);

public static class BeatmapBpmTimeline
{
    public static BeatmapBpmSegment[] ReadAll(string path)
    {
        using var map = MintAPI.rosu_pp.Beatmap.FromPath(path);
        if (map.GetRulesetAnalysisAttributes() is not { Objects: > 0 }) return [];
        using var difficulty = new MintAPI.rosu_pp.Difficulty();
        var timeline = difficulty.GetPreviewStrains(map);
        return Read(path, timeline.LastObjectMs, includeConstant: true);
    }

    public static BeatmapBpmSegment[] Read(string path, double endTimeMs, bool includeConstant = false)
    {
        var points = new SortedDictionary<double, double>();
        var inTimingPoints = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inTimingPoints = line == "[TimingPoints]";
                continue;
            }
            if (!inTimingPoints || line.Length == 0 || line.StartsWith("//")) continue;
            var fields = line.Split(',');
            if (fields.Length < 2 || (fields.Length > 6 && fields[6].Trim() != "1")) continue;
            if (!double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var beatLength) ||
                !double.IsFinite(time) || !double.IsFinite(beatLength) || beatLength <= 0 || time >= endTimeMs) continue;
            var bpm = 60000 / beatLength;
            if (double.IsFinite(bpm)) points[time] = bpm;
        }
        var starts = new List<(double Time, double Bpm)>();
        foreach (var (time, bpm) in points)
        {
            var start = Math.Max(0, time);
            if (starts.Count > 0 && starts[^1].Time == start) starts.RemoveAt(starts.Count - 1);
            if (starts.Count == 0 || Math.Abs(starts[^1].Bpm - bpm) > .001) starts.Add((start, bpm));
        }
        // A constant BPM requires no chart annotation.
        if (starts.Count == 0 || (!includeConstant && starts.Count < 2)) return [];
        return starts.Select((point, i) => new BeatmapBpmSegment(point.Time,
            i + 1 < starts.Count ? starts[i + 1].Time : endTimeMs, point.Bpm)).ToArray();
    }
}
