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
            db.Users.Add(new UserModel { Platform = "Test", PlatformUid = "123", OsuUid = "6764156", GameMode = 0 });
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
        var history = new HistoryService(db, api, null!, null!, null!, NullLogger<HistoryService>.Instance);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(ScoreController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new ScoreController(db, api, null!, null!, history, NullLogger<ScoreController>.Instance));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/score/user_score?platform=Test&platform_uid=123&beatmap_id=1949106");
            request.Headers.Accept.ParseAdd("image/png");
            using var response = await http.SendAsync(request);
            await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-diagnostic");
        }
        finally { await app.StopAsync(); }
    }
}
