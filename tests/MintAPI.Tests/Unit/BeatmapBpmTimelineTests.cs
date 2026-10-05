using MintAPI.Services;

namespace MintAPI.Tests.Unit;

public class BeatmapBpmTimelineTests
{
    [Fact]
    public void Constant_bpm_with_inherited_and_repeated_points_has_no_annotation()
    {
        Assert.Empty(Read("""
            -100,500,4,1,0,100,1,0
            200,-50,4,1,0,100,0,0
            400,500,4,1,0,100,1,0
            """));
    }

    [Fact]
    public void Changes_keep_effective_initial_bpm_merge_duplicates_and_ignore_points_outside_chart()
    {
        var segments = Read("""
            -200,1000,4,1,0,100,1,0
            -100,500,4,1,0,100,1,0
            100,500,4,1,0,100,1,0
            200,-50,4,1,0,100,0,0
            400,400,4,1,0,100,1,0
            600,400,4,1,0,100,1,0
            800,500,4,1,0,100,1,0
            1000,250,4,1,0,100,1,0
            """);
        Assert.Equal(new[] { new BeatmapBpmSegment(0, 400, 120),
            new BeatmapBpmSegment(400, 800, 150), new BeatmapBpmSegment(800, 1000, 120) }, segments);
    }

    [Fact]
    public void Detailed_view_keeps_a_single_constant_bpm_segment()
    {
        Assert.Equal(new[] { new BeatmapBpmSegment(0, 1000, 120) },
            Read("-100,500,4,1,0,100,1,0", includeConstant: true));
    }

    private static BeatmapBpmSegment[] Read(string points, bool includeConstant = false)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"[TimingPoints]\n{points}\n[HitObjects]\n256,192,500,1,0");
            return BeatmapBpmTimeline.Read(path, 1000, includeConstant);
        }
        finally { File.Delete(path); }
    }
}
