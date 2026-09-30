using Ossapi.Models;

namespace HitCircleAPI.Rendering.PerformanceAnalyzeTheme;

public sealed record AnalysisBar(string Label, int Count, double Height, bool Highlight);
public sealed record AnalysisShare(string Label, double Value, double Width);
public sealed record AnalysisGrade(string Label, int Count, double Percent, string Color);
public sealed record AnalysisPoint(double X, double Y, string Color);

public sealed class PerformanceAnalyzeData
{
    public int Count { get; init; }
    public double WeightedPp { get; init; }
    public double RawPp { get; init; }
    public double AverageAccuracy { get; init; }
    public bool HasAccuracy { get; init; }
    public double AverageStars { get; init; }
    public bool HasStars { get; init; }
    public double AverageBpm { get; init; }
    public bool HasBpm { get; init; }
    public double AverageLength { get; init; }
    public bool HasLength { get; init; }
    public double TopTenShare { get; init; }
    public double PpDecay { get; init; }
    public double MinStars { get; init; }
    public double MaxStars { get; init; }
    public string TopMod { get; init; } = "—";
    public string TopMapper { get; init; } = "—";
    public string PeakStarBucket { get; init; } = "—";
    public IReadOnlyList<double> PpValues { get; init; } = [];
    public IReadOnlyList<AnalysisGrade> Grades { get; init; } = [];
    public IReadOnlyList<AnalysisShare> Mods { get; init; } = [];
    public IReadOnlyList<AnalysisShare> Mappers { get; init; } = [];
    public IReadOnlyList<AnalysisBar> TimeBars { get; init; } = [];
    public IReadOnlyList<AnalysisBar> AccuracyBars { get; init; } = [];
    public IReadOnlyList<AnalysisBar> BpmBars { get; init; } = [];
    public IReadOnlyList<AnalysisPoint> Scatter { get; init; } = [];

    private static readonly (string Label, string Color)[] GradeOrder =
    [
        ("SSH", "#7d9bb2"), ("SS", "#cf9d42"), ("SH", "#8db4c9"), ("S", "#ddb864"),
        ("A", "#81a882"), ("B", "#b2a276"), ("C", "#c79777"), ("D", "#b77e7e")
    ];

    public static PerformanceAnalyzeData Build(IReadOnlyList<Score> scores, IReadOnlyList<double> stars)
    {
        if (scores.Count != stars.Count)
            throw new ArgumentException("Scores and star ratings must have the same length.", nameof(stars));

        var ordered = scores.Select((score, index) => (score, star: stars[index]))
            .OrderByDescending(item => item.score.Pp ?? 0).Take(100).ToList();
        var pp = ordered.Select(item => item.score.Pp is { } value && double.IsFinite(value)
            ? Math.Max(0, value) : 0).ToArray();
        var weighted = ordered.Select((item, index) => pp[index] * Math.Pow(0.95, index)).ToArray();
        var weightedTotal = weighted.Sum();
        var validStars = ordered.Select(item => item.star).Where(value => double.IsFinite(value) && value > 0).ToArray();
        var bpms = ordered.Select(item => item.score.Beatmap?.Bpm ?? 0).Where(value => value > 0).ToArray();
        var lengths = ordered.Select(item => AdjustLength(item.score)).Where(value => value > 0).ToArray();
        var accuracies = ordered.Select(item => item.score.Accuracy * 100).Where(value => double.IsFinite(value) && value >= 0 && value <= 100).ToArray();

        var modContributions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var mapperContributions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Count; index++)
        {
            var score = ordered[index].score;
            var mods = score.Mods?.Select(mod => mod.Acronym).Where(mod => !string.IsNullOrWhiteSpace(mod)).Distinct().ToList() ?? [];
            if (mods.Count == 0) mods.Add("NM");
            foreach (var mod in mods)
                modContributions[mod] = modContributions.GetValueOrDefault(mod) + weighted[index];

            var mapper = score.Beatmapset?.Creator;
            if (string.IsNullOrWhiteSpace(mapper))
                mapper = score.Beatmap?.Beatmapset?.Creator;
            if (string.IsNullOrWhiteSpace(mapper) && score.Beatmap?.UserId > 0)
                mapper = $"UID {score.Beatmap.UserId}";
            if (!string.IsNullOrWhiteSpace(mapper))
                mapperContributions[mapper] = mapperContributions.GetValueOrDefault(mapper) + weighted[index];
        }

        var grades = GradeOrder.Select(grade =>
        {
            var count = ordered.Count(item => item.score.Rank.ToString().Equals(grade.Label, StringComparison.OrdinalIgnoreCase));
            return new AnalysisGrade(grade.Label, count, ordered.Count == 0 ? 0 : count * 100.0 / ordered.Count, grade.Color);
        }).ToArray();

        var starBuckets = ordered.Where(item => item.star > 0 && double.IsFinite(item.star))
            .GroupBy(item => Math.Floor(item.star * 2) / 2)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key).FirstOrDefault();
        var scatterStars = validStars.Length > 0 ? Math.Max(7, Math.Ceiling(validStars.Max())) : 7;
        var scatterPp = pp.Length > 0 ? Math.Max(100, Math.Ceiling(pp.Max() / 100) * 100) : 100;
        var scatter = ordered.Where(item => item.star > 0 && double.IsFinite(item.star)).Select(item =>
            new AnalysisPoint(Math.Clamp(item.star / scatterStars * 100, 0, 100),
                Math.Clamp((item.score.Pp ?? 0) / scatterPp * 100, 0, 100),
                GradeOrder.FirstOrDefault(grade => grade.Label == item.score.Rank.ToString()).Color ?? "#aaa"))
            .ToArray();

        return new PerformanceAnalyzeData
        {
            Count = ordered.Count,
            WeightedPp = weightedTotal,
            RawPp = pp.Sum(),
            AverageAccuracy = accuracies.Length > 0 ? accuracies.Average() : 0,
            HasAccuracy = accuracies.Length > 0,
            AverageStars = validStars.Length > 0 ? validStars.Average() : 0,
            HasStars = validStars.Length > 0,
            AverageBpm = bpms.Length > 0 ? bpms.Average() : 0,
            HasBpm = bpms.Length > 0,
            AverageLength = lengths.Length > 0 ? lengths.Average() : 0,
            HasLength = lengths.Length > 0,
            TopTenShare = weightedTotal > 0 ? weighted.Take(10).Sum() / weightedTotal * 100 : 0,
            PpDecay = pp.Length > 1 && pp[0] > 0 ? (1 - pp[^1] / pp[0]) * 100 : 0,
            MinStars = validStars.Length > 0 ? validStars.Min() : 0,
            MaxStars = validStars.Length > 0 ? validStars.Max() : 0,
            TopMod = modContributions.OrderByDescending(item => item.Value).FirstOrDefault().Key ?? "—",
            TopMapper = mapperContributions.OrderByDescending(item => item.Value).FirstOrDefault().Key ?? "—",
            PeakStarBucket = starBuckets is null ? "—" : $"{starBuckets.Key:0.0}–{starBuckets.Key + 0.5:0.0}★ · {starBuckets.Count()} 张",
            PpValues = pp,
            Grades = grades,
            Mods = MakeShares(modContributions),
            Mappers = MakeShares(mapperContributions),
            TimeBars = BuildHistogram(FillNumericGaps(ordered.Where(item => item.score.EndedAt != default)
                .GroupBy(item => (item.score.EndedAt.Year, Quarter: (item.score.EndedAt.Month - 1) / 3 + 1))
                .OrderBy(group => group.Key.Year).ThenBy(group => group.Key.Quarter)
                .Select(group => (group.Key.Year * 4 + group.Key.Quarter - 1, group.Count())).ToList(),
                index => $"{index / 4}Q{index % 4 + 1}")),
            AccuracyBars = BuildAccuracyHistogram(accuracies),
            BpmBars = BuildHistogram(FillNumericGaps(bpms.GroupBy(value => (int)Math.Floor(value / 10))
                .OrderBy(group => group.Key).Select(group => (group.Key, group.Count())).ToList(),
                index => $"{index * 10}")),
            Scatter = scatter
        };
    }

    private static double AdjustLength(Score score)
    {
        var length = score.Beatmap?.TotalLength ?? 0;
        if (score.Mods?.Any(mod => mod.Acronym is "DT" or "NC") == true) return length / 1.5;
        if (score.Mods?.Any(mod => mod.Acronym == "HT") == true) return length / 0.75;
        return length;
    }

    private static IReadOnlyList<AnalysisShare> MakeShares(Dictionary<string, double> contributions)
    {
        var rows = contributions.OrderByDescending(item => item.Value).Take(7).ToArray();
        var maximum = rows.FirstOrDefault().Value;
        return rows.Select(item => new AnalysisShare(item.Key, item.Value, maximum > 0 ? item.Value / maximum * 100 : 0)).ToArray();
    }

    private static IReadOnlyList<AnalysisBar> BuildAccuracyHistogram(double[] values)
    {
        if (values.Length == 0) return [];
        var minimum = Math.Min(99.5, Math.Floor(values.Min() * 2) / 2);
        var bucketCount = Math.Max(1, (int)Math.Ceiling((100 - minimum) / 0.5));
        var counts = new int[bucketCount];
        foreach (var value in values)
            counts[Math.Clamp((int)Math.Floor((value - minimum) / 0.5), 0, bucketCount - 1)]++;
        return BuildHistogram(counts.Select((count, index) => ($"{minimum + index * 0.5:0.0}%", count)).ToList());
    }

    private static IReadOnlyList<AnalysisBar> BuildHistogram(IReadOnlyList<(string Label, int Count)> entries)
    {
        var maximum = entries.Count == 0 ? 0 : entries.Max(item => item.Count);
        var highlighted = entries.Count == 0 ? -1 : Enumerable.Range(0, entries.Count).First(index => entries[index].Count == maximum);
        return entries.Select((item, index) => new AnalysisBar(item.Label, item.Count,
            maximum > 0 ? item.Count * 100.0 / maximum : 0, index == highlighted)).ToArray();
    }

    private static IReadOnlyList<(string Label, int Count)> FillNumericGaps(
        IReadOnlyList<(int Index, int Count)> entries, Func<int, string> label)
    {
        if (entries.Count == 0) return [];
        var byIndex = entries.ToDictionary(item => item.Index, item => item.Count);
        return Enumerable.Range(entries[0].Index, entries[^1].Index - entries[0].Index + 1)
            .Select(index => (label(index), byIndex.GetValueOrDefault(index))).ToArray();
    }
}
