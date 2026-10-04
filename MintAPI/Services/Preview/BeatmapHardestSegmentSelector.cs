using System.Globalization;
using System.Text.RegularExpressions;
using MintAPI.rosu_pp;

namespace MintAPI.Services.Preview;

public record SelectedPreviewSegment(double StartSeconds, double EndSeconds, double Score, bool IsPreview = false);
public record BeatmapPreviewSelection(string Algorithm, double ClockRate, OsuStrainTimeline Timeline,
    SelectedPreviewSegment[] Segments);

/// <summary>Ranks bounded playback windows using mode-specific strain; this score is not a star rating.</summary>
public static class BeatmapHardestSegmentSelector
{
    public const string AlgorithmVersion = "native-strains-v3";

    public static BeatmapPreviewSelection Select(string path, string[] mods, double durationSeconds, bool includePreview = false)
    {
        using var map = Beatmap.FromPath(path);
        using var difficulty = new Difficulty();
        uint flags = 0;
        var rate = 1.0;
        foreach (var mod in mods)
        {
            flags |= mod switch { "ez" => 2u, "hd" => 8u, "hr" => 16u, _ => 0u };
            if (mod.StartsWith("dt")) rate = mod.Length == 2 ? 1.5 : double.Parse(mod[2..], CultureInfo.InvariantCulture);
            if (mod.StartsWith("ht")) rate = mod.Length == 2 ? .75 : double.Parse(mod[2..], CultureInfo.InvariantCulture);
            if (!mod.StartsWith("da")) continue;
            foreach (Match field in Regex.Matches(mod[2..], @"(cs|ar|od|hp)(-?\d+(?:\.\d+)?)"))
            {
                var value = float.Parse(field.Groups[2].Value, CultureInfo.InvariantCulture);
                switch (field.Groups[1].Value)
                {
                    case "cs": difficulty.Cs(value, true); break;
                    case "ar": difficulty.Ar(value, true); break;
                    case "od": difficulty.Od(value, true); break;
                    case "hp": difficulty.Hp(value, true); break;
                }
            }
        }
        difficulty.Mods(flags).ClockRate(rate);
        OsuStrainTimeline timeline;
        try { timeline = difficulty.GetPreviewStrains(map, mods.Contains("in"), mods.Contains("ho")); }
        catch (InvalidOperationException)
        {
            throw new PreviewValidationException("Cannot calculate native mode strains: the map must be valid and contain at least one object.");
        }
        return new(AlgorithmVersion, rate, timeline, Rank(timeline, durationSeconds, rate, includePreview ? ReadPreviewTime(path, timeline.FirstObjectMs) : null));
    }

    public static SelectedPreviewSegment[] Rank(OsuStrainTimeline timeline, double durationSeconds, double clockRate, double? previewTimeMs = null)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 || !double.IsFinite(clockRate) || clockRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(timeline.SectionMs) || timeline.SectionMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeline));
        var length = durationSeconds * clockRate * 1000;
        var origin = timeline.FirstObjectMs;
        var lastStart = Math.Max(origin, timeline.LastObjectMs - length);
        if (timeline.Points.Length == 0)
        {
            var start = Math.Clamp(previewTimeMs ?? origin, origin, lastStart);
            return previewTimeMs.HasValue
                ? [new SelectedPreviewSegment((start - origin) / 1000, (start + length - origin) / 1000, 0, true)]
                : [];
        }
        (double Start, double End, double Score) Score(double start)
        {
            var end = start + length;
            double total = 0, peak = 0;
            var firstIndex = Math.Clamp((int)Math.Floor((start - (timeline.Points[0].EndTimeMs - timeline.SectionMs)) / timeline.SectionMs), 0, timeline.Points.Length - 1);
            for (var i = firstIndex; i < timeline.Points.Length; i++)
            {
                var point = timeline.Points[i];
                if (point.EndTimeMs - timeline.SectionMs >= end) break;
                var overlap = Math.Max(0, Math.Min(end, point.EndTimeMs) - Math.Max(start, point.EndTimeMs - timeline.SectionMs));
                if (overlap == 0) continue;
                var strain = Math.Sqrt(point.Aim * point.Aim + point.Speed * point.Speed);
                total += strain * overlap;
                peak = Math.Max(peak, strain);
            }
            return (start, end, .5 * peak + .5 * total / length);
        }
        var candidates = timeline.Points.Select(point => Math.Clamp(point.EndTimeMs - timeline.SectionMs - 1000 * clockRate, origin, lastStart))
            .Append(origin).Append(lastStart).Distinct().Select(Score)
            .Where(candidate => candidate.Score > 0).OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Start).ToArray();
        var gap = (previewTimeMs.HasValue ? 5 : 1) * 1000 * clockRate;
        bool Conflicts((double Start, double End, double Score) a, (double Start, double End, double Score) b) =>
            a.Start < b.End + gap && a.End + gap > b.Start;
        var chosen = new List<(double Start, double End, double Score)>();
        double? previewStart = null;
        if (previewTimeMs.HasValue)
        {
            previewStart = Math.Clamp(previewTimeMs.Value, origin, lastStart);
            chosen.Add(Score(previewStart.Value));
            // Retain the three highest independent windows, replacing any that conflict with preview.
            var hardest = new List<(double Start, double End, double Score)>();
            foreach (var candidate in candidates)
            {
                if (hardest.Any(segment => Conflicts(candidate, segment))) continue;
                hardest.Add(candidate);
                if (hardest.Count == 3) break;
            }
            foreach (var candidate in hardest)
                if (!chosen.Any(segment => Conflicts(candidate, segment))) chosen.Add(candidate);
        }
        foreach (var candidate in candidates)
        {
            if (chosen.Count == 4) break;
            if (chosen.Any(segment => Conflicts(candidate, segment))) continue;
            chosen.Add(candidate);
        }
        return chosen.OrderBy(segment => segment.Start).Select(segment => new SelectedPreviewSegment(
            (segment.Start - origin) / 1000, (segment.End - origin) / 1000, segment.Score, segment.Start == previewStart)).ToArray();
    }

    private static double ReadPreviewTime(string path, double fallback)
    {
        var general = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[')) { general = line == "[General]"; continue; }
            if (!general) continue;
            var separator = line.IndexOf(':');
            if (separator < 0 || line[..separator].Trim() != "PreviewTime") continue;
            if (double.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0)
                return value;
            break;
        }
        return fallback;
    }
}
