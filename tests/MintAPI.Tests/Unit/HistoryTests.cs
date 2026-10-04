using MintAPI.Models.Entities;
using MintAPI.Services;
using MintOsuApi.Enums;

namespace MintAPI.Tests.Unit;

public sealed class HistoryTests
{
    [Fact]
    public void Merge_prefers_local_and_preserves_gaps()
    {
        var first = new DateOnly(2026, 9, 1);
        var result = HistoryService.Merge(
            [new(first, 200, 50, "local"), new(first.AddDays(2), 210, 40, "local")],
            [new(first, 100, 100, "osutrack"), new(first.AddDays(-1), 90, 120, "osutrack"), new(first.AddDays(1), 0, 0, "osutrack")]);
        Assert.Equal(3, result.Count);
        Assert.Equal(200, result[1].Pp);
        Assert.Equal("local", result[1].Source);
        Assert.DoesNotContain(result, p => p.Date == first.AddDays(1));
    }

    [Theory]
    [InlineData(RankStatus.Graveyard, false)]
    [InlineData(RankStatus.Wip, false)]
    [InlineData(RankStatus.Pending, false)]
    [InlineData(RankStatus.Ranked, true)]
    [InlineData(RankStatus.Approved, true)]
    [InlineData(RankStatus.Qualified, true)]
    [InlineData(RankStatus.Loved, true)]
    public void Leaderboard_status_controls_archive_scope(RankStatus status, bool expected) =>
        Assert.Equal(expected, HistoryService.HasLeaderboard(status));

    [Theory]
    [InlineData(11, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(9, 10, false)]
    [InlineData(10, null, false)]
    [InlineData(null, 10, false)]
    public void Active_requires_increased_play_count(int? current, int? previous, bool expected) =>
        Assert.Equal(expected, HistoryCollector.IsActive(new() { PlayCount = current }, new() { PlayCount = previous }));

    [Fact]
    public void First_snapshot_does_not_trigger_collection() =>
        Assert.False(HistoryCollector.IsActive(new() { PlayCount = 100 }, null));

    [Fact]
    public void Date_uses_configured_timezone()
    {
        var options = new HistoryOptions { TimeZone = "Asia/Shanghai" };
        Assert.Equal(new DateOnly(2026, 10, 1), options.Today(new DateTimeOffset(2026, 9, 30, 17, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("25:00", "02:00", false)]
    [InlineData("03:00", "02:00", false)]
    [InlineData("00:00", "02:00", true)]
    public void Schedule_validates_times(string info, string score, bool expected) =>
        Assert.Equal(expected, new HistoryOptions { InfoTime = info, ScoreTime = score }.IsValid());
}
