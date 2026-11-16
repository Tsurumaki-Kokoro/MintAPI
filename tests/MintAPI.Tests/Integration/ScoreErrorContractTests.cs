using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class ScoreErrorContractTests
{
    [Theory]
    [InlineData("unbound", "USER_NOT_BOUND", 404)]
    [InlineData("official-empty", "SCORE_NOT_FOUND", 404)]
    [InlineData("local-empty", "LOCAL_SCORE_NOT_COLLECTED", 404)]
    [InlineData("map-missing", "BEATMAP_NOT_FOUND", 404)]
    [InlineData("upstream", "OSU_API_UNAVAILABLE", 502)]
    public async Task User_score_distinguishes_lookup_results_from_service_failure(string scenario, string code, int status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        if (scenario != "unbound")
        {
            db.Users.Add(new UserModel { Platform = "Test", PlatformUid = "123", OsuUserId = 6764156, GameMode = 0 });
            await db.SaveChangesAsync();
        }
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, _) => new User { Id = 6764156 },
            BeatmapHandler = _ => scenario switch
            {
                "map-missing" => throw new HttpRequestException("private-diagnostic", null, System.Net.HttpStatusCode.NotFound),
                "upstream" => throw new HttpRequestException("private-diagnostic"),
                _ => new Beatmap { Id = 1949106, Status = scenario == "local-empty" ? RankStatus.Pending : RankStatus.Ranked }
            },
            BeatmapUserScoresHandler = (_, _, _) => []
        };
        await using var app = await StartAsync(db, api);
        using var http = CreateClient(app);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/score/user_score?platform=Test&platform_uid=123&beatmap_id=1949106");
        request.Headers.Accept.ParseAdd("image/png");
        using var response = await http.SendAsync(request);
        await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-diagnostic");
    }

    [Theory]
    [InlineData("ok", null, 200)]
    [InlineData("no-metadata", null, 200)]
    [InlineData("invalid-mode", "INVALID_ARGUMENT", 400)]
    [InlineData("unbound", "USER_NOT_BOUND", 404)]
    [InlineData("missing-user", "OSU_USER_NOT_FOUND", 404)]
    [InlineData("empty", "BEST_PLAY_NOT_FOUND", 404)]
    [InlineData("upstream", "OSU_API_UNAVAILABLE", 502)]
    public async Task Best_play_beatmapsets_returns_metadata_or_problem_json(string scenario, string? code, int status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        if (scenario != "unbound")
        {
            db.Users.Add(new UserModel { Platform = "Test", PlatformUid = "123", OsuUserId = 42, GameMode = 3 });
            await db.SaveChangesAsync();
        }
        var beatmapset = new BeatmapsetCompact { Id = 5, Title = "Song", TitleUnicode = "曲", Artist = "Artist" };
        var api = new RecordingOsuApiService
        {
            ScoresHandler = (uid, type, mode, limit, offset, _, _) =>
            {
                Assert.Equal((42, ScoreType.Best, (GameMode?)GameMode.Mania, 100, 0), (uid, type, mode, limit, offset));
                return scenario switch
                {
                    "upstream" => throw new HttpRequestException("private-diagnostic"),
                    "missing-user" => throw new HttpRequestException("private-diagnostic", null, System.Net.HttpStatusCode.NotFound),
                    "empty" => [],
                    "no-metadata" => [new Score()],
                    _ => [new Score { Beatmapset = beatmapset, User = new UserCompact { Username = "Player" } },
                          new Score { Beatmapset = beatmapset, Beatmap = new Beatmap { Id = 23, Mode = GameMode.Osu } }, new Score()]
                };
            }
        };
        await using var app = await StartAsync(db, api);
        using var http = CreateClient(app);
        var path = "/score/best_play_beatmapsets?platform=Test&platform_uid=123";
        if (scenario == "invalid-mode")
            path += "&game_mode=4";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd(code is null ? "application/json" : "image/png");
        using var response = await http.SendAsync(request);
        if (code is not null)
        {
            await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-diagnostic");
            return;
        }

        response.EnsureSuccessStatusCode();
        var result = Assert.IsType<ScoreController.BestPlayBeatmapsetsResponse>(
            await response.Content.ReadFromJsonAsync<ScoreController.BestPlayBeatmapsetsResponse>());
        Assert.Equal(42, result.UserId);
        Assert.Equal(3, result.GameMode);
        if (scenario == "no-metadata")
        {
            Assert.Empty(result.Beatmapsets);
            Assert.Null(result.Username);
        }
        else
        {
            Assert.Equal("Player", result.Username);
            var set = Assert.Single(result.Beatmapsets);
            Assert.Equal((5, "Song", "曲", "Artist"), (set.Id, set.Title, set.TitleUnicode, set.Artist));
            Assert.Equal(23, set.BeatmapId);
            Assert.Equal(0, set.BeatmapMode);
        }
    }

    private static async Task<WebApplication> StartAsync(AppDbContext db, RecordingOsuApiService api)
    {
        var history = new HistoryService(db, api, null!, null!, null!, NullLogger<HistoryService>.Instance);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(ScoreController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new ScoreController(db, api, null!, null!, history, NullLogger<ScoreController>.Instance));
        var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => new() { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First()) };
}
