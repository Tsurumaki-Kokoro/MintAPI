using HitCircleAPI.rosu_pp;
using HitCircleAPI.Services.Preview;

namespace HitCircleAPI.Tests.Unit;

public sealed class BeatmapHardestSegmentSelectorTests
{
    [Fact]
    public void Sustained_bursts_are_selected_without_overlap_and_with_origin_removed()
    {
        var points = Enumerable.Range(0, 400).Select(i => new OsuStrainPoint
        {
            EndTimeMs = 1200 + i * 400,
            Aim = i is >= 30 and <= 40 or >= 100 and <= 110 or >= 200 and <= 210 or >= 300 and <= 310 ? 100 : 1,
            Speed = 0
        }).ToArray();
        var selected = BeatmapHardestSegmentSelector.Rank(new(1000, 160000, 400, points), 6, 1);
        Assert.Equal(4, selected.Length);
        Assert.All(selected, segment => Assert.True(segment.Score > 50));
        for (var i = 1; i < selected.Length; i++) Assert.True(selected[i].StartSeconds >= selected[i - 1].EndSeconds + 1);
        Assert.InRange(selected[0].StartSeconds, 6, 13);
        Assert.InRange(selected[1].StartSeconds, 34, 41);
    }

    [Fact]
    public void Short_map_produces_one_window_and_DT_scales_chart_duration()
    {
        var timeline = new OsuStrainTimeline(5000, 8000, 600,
            [new() { EndTimeMs = 6000, Aim = 10 }, new() { EndTimeMs = 6600, Aim = 20 }]);
        var segment = Assert.Single(BeatmapHardestSegmentSelector.Rank(timeline, 6, 1.5));
        Assert.Equal(0, segment.StartSeconds);
        Assert.Equal(9, segment.EndSeconds);
    }

    [Fact]
    public void Manual_points_override_hardest_and_conversions_are_rejected()
    {
        var manual = PreviewRequestValidator.Normalize(new(1, "gif", null, [], ["30"], 6, "hardest"), new());
        Assert.Equal("auto", manual.Selection);
        var automatic = PreviewRequestValidator.Normalize(new(1, "gif", null, [], [], 6, "hardest"), new());
        PreviewRequestValidator.ValidateMode(automatic, 1);
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.ValidateMode(automatic with { Convert = "taiko" }, 0));
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.Normalize(automatic with { Format = "png" }, new()));
    }

    [Fact]
    public void Preview_only_is_retained_when_short_map_cannot_fit_distant_segments()
    {
        var timeline = new OsuStrainTimeline(1000, 3000, 400, [new() { EndTimeMs = 2000, Aim = 20 }]);
        var selected = Assert.Single(BeatmapHardestSegmentSelector.Rank(timeline, 6, 1, 2500));
        Assert.True(selected.IsPreview);
        Assert.Equal(0, selected.StartSeconds);
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.0, true)]
    [InlineData(1.5, true)]
    public void Preview_is_retained_and_collision_is_replaced_with_a_distant_window(double rate, bool collide)
    {
        var points = Enumerable.Range(0, 500).Select(i => new OsuStrainPoint
        {
            EndTimeMs = 1000 + (i + 1) * 400 * rate,
            Aim = i is >= 30 and <= 40 ? 120 : i is >= 100 and <= 110 ? 110 : i is >= 200 and <= 210 ? 100 : i is >= 300 and <= 310 ? 90 : 1
        }).ToArray();
        var timeline = new OsuStrainTimeline(1000, 1000 + 200000 * rate, 400 * rate, points);
        var preview = 1000 + (collide ? 12 : 60) * 1000 * rate;
        var selected = BeatmapHardestSegmentSelector.Rank(timeline, 6, rate, preview);
        Assert.Equal(4, selected.Length);
        var previewSegment = Assert.Single(selected, segment => segment.IsPreview);
        Assert.Equal((preview - 1000) / 1000, previewSegment.StartSeconds, 6);
        for (var index = 1; index < selected.Length; index++)
            Assert.True(selected[index].StartSeconds >= selected[index - 1].EndSeconds + 5 * rate - .000001);
        if (collide)
            Assert.Contains(selected, segment => segment.StartSeconds >= 114 * rate && segment.StartSeconds <= 124 * rate);
        else
            Assert.DoesNotContain(selected.Where(segment => !segment.IsPreview), segment => segment.StartSeconds >= 114 * rate && segment.StartSeconds <= 124 * rate);
    }

    [Theory]
    [InlineData(1.0, 2000, 400)]
    [InlineData(1.5, 2400, 600)]
    [InlineData(.75, 2100, 300)]
    public void Native_strain_timestamps_follow_second_object_section_and_clock_rate(double rate, double firstEnd, double section)
    {
        var text = "osu file format v14\n[General]\nMode:0\n[Difficulty]\nCircleSize:4\nOverallDifficulty:5\nApproachRate:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n64,192,1100,1,0,0:0:0:0:\n448,192,1900,1,0,0:0:0:0:\n64,192,6000,1,0,0:0:0:0:\n448,192,6100,1,0,0:0:0:0:\n64,192,6200,1,0,0:0:0:0:\n";
        using var map = Beatmap.FromBytes(System.Text.Encoding.UTF8.GetBytes(text));
        using var difficulty = new Difficulty();
        var strains = difficulty.ClockRate(rate).GetOsuStrains(map);
        Assert.Equal(1100, strains.FirstObjectMs);
        Assert.Equal(6200, strains.LastObjectMs);
        Assert.Equal(firstEnd, strains.Points[0].EndTimeMs, 6);
        Assert.Equal(section, strains.SectionMs, 6);
        Assert.Contains(strains.Points, point => point.Aim > 0 || point.Speed > 0);
        for (var i = 1; i < strains.Points.Length; i++)
            Assert.Equal(section, strains.Points[i].EndTimeMs - strains.Points[i - 1].EndTimeMs, 6);
    }
    [Theory]
    [InlineData("1028484", 1.0)]
    [InlineData("1028484", 1.5)]
    [InlineData("1028484", .75)]
    [InlineData("2118524", 1.0)]
    [InlineData("2118524", 1.5)]
    [InlineData("2118524", .75)]
    [InlineData("1638954", 1.0)]
    [InlineData("1638954", 1.5)]
    [InlineData("1638954", .75)]
    public void Native_modes_preserve_preview_and_three_separated_windows(string id, double rate)
    {
        var mods = rate == 1 ? Array.Empty<string>() : new[] { rate > 1 ? "dt" : "ht" };
        var selection = BeatmapHardestSegmentSelector.Select(Path.Combine(AppContext.BaseDirectory, "Fixtures", id + ".osu"), mods, 6, true);
        Assert.Equal(id == "1638954" && rate == 1.5 ? 3 : 4, selection.Segments.Length);
        Assert.Single(selection.Segments, segment => segment.IsPreview);
        Assert.All(selection.Segments, segment => Assert.Equal(6 * rate, segment.EndSeconds - segment.StartSeconds, 6));
        for (var n = 1; n < selection.Segments.Length; n++)
            Assert.True(selection.Segments[n].StartSeconds >= selection.Segments[n - 1].EndSeconds + 5 * rate - .000001);
        Assert.Contains(selection.Timeline.Points, point => point.Aim > 0);
    }

    [Theory]
    [InlineData(1, 6000, 6200)]
    [InlineData(2, 2250, 6200)]
    [InlineData(3, 2000, 12000)]
    public void Mode_timestamps_use_processed_objects_and_hold_tail(int mode, double firstEnd, double last)
    {
        var first = mode == 2 ? "64,192,1100,2,0,L|344:192,1,280" : "64,192,1100,1,0,0:0:0:0:";
        var tail = mode == 3 ? "64,192,6200,128,0,12000:0:0:0:0:" : "64,192,6200,1,0,0:0:0:0:";
        var text = $"osu file format v14\n[General]\nMode:{mode}\n[Difficulty]\nCircleSize:4\nOverallDifficulty:5\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n{first}\n448,192,1900,1,0,0:0:0:0:\n64,192,6000,1,0,0:0:0:0:\n448,192,6100,1,0,0:0:0:0:\n{tail}\n";
        using var map = Beatmap.FromBytes(System.Text.Encoding.UTF8.GetBytes(text));
        using var difficulty = new Difficulty();
        var timeline = difficulty.GetPreviewStrains(map);
        Assert.Equal(firstEnd, timeline.Points[0].EndTimeMs, 6);
        Assert.Equal(last, timeline.LastObjectMs, 6);
    }

    [Fact]
    public void Mania_hold_off_removes_hold_tail_and_inverse_changes_curve()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "1638954.osu");
        var original = BeatmapHardestSegmentSelector.Select(path, [], 6, true);
        var holdOff = BeatmapHardestSegmentSelector.Select(path, ["ho"], 6, true);
        var inverse = BeatmapHardestSegmentSelector.Select(path, ["in"], 6, true);
        Assert.False(original.Timeline.Points.Select(p => p.Aim).SequenceEqual(holdOff.Timeline.Points.Select(p => p.Aim)));
        Assert.False(original.Timeline.Points.Select(p => p.Aim).SequenceEqual(inverse.Timeline.Points.Select(p => p.Aim)));
        Assert.Single(inverse.Segments, segment => segment.IsPreview);
    }

    [Fact]
    public void Empty_strain_timeline_still_preserves_bounded_preview_time()
    {
        var selected = Assert.Single(BeatmapHardestSegmentSelector.Rank(new(1000, 60000, 750, []), 6, 1, 30000));
        Assert.Equal(29, selected.StartSeconds);
        Assert.True(selected.IsPreview);
    }

}
