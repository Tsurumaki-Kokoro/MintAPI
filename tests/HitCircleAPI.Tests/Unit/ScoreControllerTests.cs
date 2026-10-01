using HitCircleAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitCircleAPI.Tests.Unit;

public class ScoreControllerTests
{
    private static ScoreController Controller() => new(null!, null!, null!, null!, null!, NullLogger<ScoreController>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Invalid_indexes_are_rejected_before_accessing_services(int index)
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().RecentPlay("qq", "1", recent_index: index));
        Assert.IsType<BadRequestObjectResult>(await Controller().BestPlay("qq", "1", best_index: index));
    }

    [Theory]
    [InlineData(10, 9)]
    [InlineData(1, 21)]
    [InlineData(90, 101)]
    public async Task Invalid_bp_ranges_are_rejected(int first, int last)
        => Assert.IsType<BadRequestObjectResult>(await Controller().BestPlay("qq", "1", best_index: first, best_end: last));

    [Theory]
    [InlineData(10, 9)]
    [InlineData(1, 21)]
    [InlineData(90, 101)]
    public async Task Invalid_recent_ranges_are_rejected(int first, int last)
        => Assert.IsType<BadRequestObjectResult>(await Controller().RecentPlay("qq", "1", recent_index: first, recent_end: last));

    [Fact]
    public async Task Removed_theme_and_invalid_mode_are_rejected()
    {
        Assert.IsType<BadRequestObjectResult>(await Controller().RecentPlay("qq", "1", theme: "apple"));
        Assert.IsType<BadRequestObjectResult>(await Controller().BestPlay("qq", "1", game_mode: 4));
        Assert.IsType<BadRequestObjectResult>(await Controller().BestPlay("qq", "1", theme: "yaowan", best_end: 5));
        Assert.IsType<BadRequestObjectResult>(await Controller().RecentPlay("qq", "1", theme: "yaowan", recent_end: 5));
    }
}
