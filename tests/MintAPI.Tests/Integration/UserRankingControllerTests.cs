using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Services;
using MintAPI.Models.Entities;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class UserRankingControllerTests
{
    [Fact]
    public async Task Rankings_sort_each_mode_limit_top_five_and_validate_bindings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        for (var i = 1; i <= 7; i++)
            db.Users.Add(new UserModel { Platform = "qq", PlatformUid = i.ToString(), OsuUid = i.ToString() });
        await db.SaveChangesAsync();
        var api = new RecordingOsuApiService { UserHandler = (id, mode) => new User
        {
            Id = int.Parse(id), Username = "user" + id, AvatarUrl = "https://a.ppy.sh/" + id, CountryCode = "CN",
            Statistics = new UserStatistics { GlobalRank = id == "7" ? null : (int)mode! % 2 == 0 ? 7 - int.Parse(id) : int.Parse(id), Pp = 1234, HitAccuracy = 98.5 }
        } };
        var controller = new UserRankingController(db, api, new UserRankingRenderer(new Capture(), new AvatarCardImageCache([]))) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var request = new UserRankingRequest("qq", ["1", "2", "3", "4", "5", "6", "7", "1"]);
        var single = Assert.IsType<UserModeRanking>(Assert.IsType<OkObjectResult>(await controller.Ranking(request, 0, "json")).Value);
        Assert.Equal(new[] { "6", "5", "4", "3", "2", "1", "7" }, single.Users.Select(u => u.PlatformUid));
        Assert.Null(single.Users.Last().Rank);
        Assert.Equal("CN", single.Users[0].CountryCode);
        Assert.Equal("https://a.ppy.sh/6", single.Users[0].AvatarUrl);
        Assert.Equal(98.5, single.Users[0].Acc);
        Assert.Equal(1234, single.Users[0].Pp);
        Assert.Equal(7, api.CallCount);
        var modes = Assert.IsType<List<UserModeRanking>>(Assert.IsType<OkObjectResult>(await controller.TopFive(request, "json")).Value);
        Assert.Equal(new[] { 0, 1, 2, 3 }, modes.Select(m => m.GameMode));
        Assert.All(modes, m => Assert.Equal(5, m.Users.Count));
        Assert.Equal("6", modes[0].Users[0].PlatformUid);
        Assert.Equal("1", modes[1].Users[0].PlatformUid);
        var small = Assert.IsType<List<UserModeRanking>>(Assert.IsType<OkObjectResult>(await controller.TopFive(new("qq", ["1"]), "json")).Value);
        Assert.All(small, m => Assert.Single(m.Users));
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(await controller.Ranking(request, 0)).ContentType);
        Assert.Equal("image/png", Assert.IsType<FileContentResult>(await controller.TopFive(request)).ContentType);
        var calls = api.CallCount;
        Assert.IsType<NotFoundObjectResult>(await controller.Ranking(new("qq", ["missing"]), 0));
        Assert.IsType<BadRequestObjectResult>(await controller.Ranking(request, 4));
        Assert.IsType<BadRequestObjectResult>(await controller.Ranking(new("qq", []), 0));
        Assert.Equal(calls, api.CallCount);
    }
    private sealed class Capture : IRenderService
    {
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default) => Task.FromResult(new byte[] { 1 });
    }
}
