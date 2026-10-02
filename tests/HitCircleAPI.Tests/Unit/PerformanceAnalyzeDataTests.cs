using HitCircleAPI.Rendering.PerformanceAnalyzeTheme;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Tests.Unit;

public class PerformanceAnalyzeDataTests
{
    [Fact]
    public void Build_uses_percent_accuracy_weighted_mods_and_adjusted_lengths()
    {
        var scores = new List<Score>
        {
            new() { Pp = 200, Accuracy = 0.98, Rank = Grade.S, EndedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), Mods = [new NonLegacyMod { Acronym = "DT" }], Beatmap = new Beatmap { DifficultyRating = 5, TotalLength = 180, Bpm = 180 }, Beatmapset = new BeatmapsetCompact { Creator = "Mapper A" } },
            new() { Pp = 100, Accuracy = 0.96, Rank = Grade.A, EndedAt = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), Beatmap = new Beatmap { DifficultyRating = 4, TotalLength = 120, Bpm = 160 }, Beatmapset = new BeatmapsetCompact { Creator = "Mapper B" } }
        };

        var report = PerformanceAnalyzeData.Build(scores, [5, 4]);

        Assert.Equal(295, report.WeightedPp, 5);
        Assert.Equal(97, report.AverageAccuracy, 5);
        Assert.Equal(120, report.AverageLength, 5);
        Assert.Equal("DT", report.TopMod);
        Assert.Equal("Mapper A", report.TopMapper);
        Assert.Equal(2, report.ComboBars.Sum(bar => bar.Count));
        Assert.Equal(2, report.Grades.Sum(grade => grade.Count));
    }

    [Fact]
    public void Build_marks_missing_beatmap_metrics_as_unavailable()
    {
        var report = PerformanceAnalyzeData.Build([new Score { Pp = 100, Accuracy = 0.98 }], [0]);

        Assert.False(report.HasStars);
        Assert.False(report.HasBpm);
        Assert.False(report.HasLength);
        Assert.Empty(report.BpmBars);
        Assert.Empty(report.Scatter);
    }

    [Fact]
    public void Build_groups_achieved_combo_with_empty_buckets_and_exact_boundaries()
    {
        var combos = new[] { 0, 249, 250, 499, 500, 1000, -1 };
        var scores = combos.Select(combo => new Score { MaxCombo = combo, Pp = 100 }).ToArray();

        var report = PerformanceAnalyzeData.Build(scores, new double[scores.Length]);

        Assert.Equal(250, report.ComboBucketSize);
        Assert.Equal(new[] { 2, 2, 1, 0, 1 }, report.ComboBars.Select(bar => bar.Count));
        Assert.Equal(new[] { "0", "250", "500", "750", "1,000" }, report.ComboBars.Select(bar => bar.Label));
        Assert.Equal(6, report.ComboBars.Sum(bar => bar.Count));
    }

    [Fact]
    public void Build_keeps_combo_histogram_bounded_for_long_maps_and_empty_reports()
    {
        var report = PerformanceAnalyzeData.Build([new Score { MaxCombo = int.MaxValue }], [0]);

        Assert.InRange(report.ComboBars.Count, 1, 16);
        Assert.Equal(1, report.ComboBars.Sum(bar => bar.Count));
        Assert.True(report.ComboBucketSize > 250);
        Assert.Empty(PerformanceAnalyzeData.Build([], []).ComboBars);
    }
}
