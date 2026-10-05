using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using MintAPI.Services;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public class NewBestPlayDataTests
{
    [Fact]
    public void Time_filter_uses_rolling_window_retains_BP_rank_and_filters_before_paging()
    {
        var now = DateTimeOffset.Parse("2026-10-04T12:00:00+08:00");
        var scores = new List<Score>
        {
            new() { EndedAt = now.AddDays(-2) },
            new() { EndedAt = now.AddHours(-23) },
            new() { EndedAt = now.AddDays(-1) },
            new() { EndedAt = now.AddHours(-2) },
            new() { EndedAt = now.AddHours(1) }
        };
        var report = NewBestPlayData.Select(scores, now, 1, 2, 3, []);
        Assert.Equal(2, report.Total);
        Assert.Equal(4, Assert.Single(report.Entries).BpRank);
        Assert.Same(scores[3], report.Entries[0].Score);
    }

    [Fact]
    public void Mods_match_by_inclusion_and_NM_ignores_CL()
    {
        var now = DateTimeOffset.UtcNow;
        var scores = new List<Score>
        {
            new() { EndedAt = now, Mods = [new() { Acronym = "CL" }] },
            new() { EndedAt = now, Mods = [new() { Acronym = "HD" }, new() { Acronym = "HR" }] }
        };
        Assert.Equal(1, Assert.Single(NewBestPlayData.Select(scores, now, 1, 1, 20, ["NM"]).Entries).BpRank);
        Assert.Equal(2, Assert.Single(NewBestPlayData.Select(scores, now, 1, 1, 20, ["HD"]).Entries).BpRank);
        Assert.Empty(NewBestPlayData.Select(scores, now, 1, 1, 20, ["DT"]).Entries);
    }

    [Theory]
    [InlineData(0, 1, 20, null)]
    [InlineData(366, 1, 20, null)]
    [InlineData(1, 2, 1, null)]
    [InlineData(1, 1, 21, null)]
    [InlineData(1, 190, 201, null)]
    [InlineData(1, 1, 20, "NM,HD")]
    [InlineData(1, 1, 20, "<script>")]
    public async Task Invalid_parameters_fail_before_accessing_services(int days, int first, int last, string? mods)
    {
        var controller = new ScoreController(null!, null!, null!, null!, null!, NullLogger<ScoreController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await controller.NewBestPlays("qq", "1", days, mods: mods, first: first, last: last));
    }
}
