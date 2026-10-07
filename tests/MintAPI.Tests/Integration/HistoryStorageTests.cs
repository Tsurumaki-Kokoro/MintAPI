using System.Net;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public sealed class HistoryStorageTests
{
    [Fact]
    public async Task Official_map_scores_fill_missing_metadata_without_replacing_existing_details()
    {
        var map = new Beatmap { Id = 1949106, Status = RankStatus.Ranked, Beatmapset = new Beatmapset { Id = 1 } };
        var existingMap = new Beatmap { Id = map.Id, Beatmapset = new Beatmapset { Id = 2 } };
        var existingSet = new Beatmapset { Id = 3 };
        var scores = new List<Score>
        {
            new() { BeatmapId = map.Id },
            new() { Beatmap = existingMap },
            new() { Beatmapset = existingSet }
        };
        var api = new RecordingOsuApiService
        {
            BeatmapHandler = id => { Assert.Equal(map.Id, id); return map; },
            BeatmapUserScoresHandler = (id, userId, mode) =>
            {
                Assert.Equal(map.Id, id);
                Assert.Equal(6764156, userId);
                Assert.Equal(GameMode.Osu, mode);
                return scores;
            }
        };
        var service = new HistoryService(null!, api, null!, null!, null!, NullLogger<HistoryService>.Instance);

        var result = await service.GetMapScoresAsync(6764156, map.Id, 0, default);

        Assert.Equal("official", result.Source);
        Assert.Same(scores, result.Scores);
        Assert.Same(map, scores[0].Beatmap);
        Assert.Same(map.Beatmapset, scores[0].Beatmapset);
        Assert.Same(existingMap, scores[1].Beatmap);
        Assert.Same(existingMap.Beatmapset, scores[1].Beatmapset);
        Assert.Same(map, scores[2].Beatmap);
        Assert.Same(existingSet, scores[2].Beatmapset);
        Assert.Equal(2, api.CallCount);
    }

    private sealed class Factory(HttpStatusCode status, string body) : IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(() =>
        {
            Calls++;
            return new(status) { Content = new StringContent(body) };
        })) { BaseAddress = new Uri("https://example.test/") };
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response());
    }

    [Fact]
    public async Task Archive_is_idempotent_and_keeps_only_unranked_scores()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HistoryService(db, new RecordingOsuApiService(), new Factory(HttpStatusCode.NotFound, ""), cache,
            Options.Create(new HistoryOptions()), NullLogger<HistoryService>.Instance);
        var unranked = new Score { Id = 1, UserId = 2, RulesetId = 3, EndedAt = DateTimeOffset.UtcNow,
            Beatmap = new Beatmap { Id = 4, Status = RankStatus.Pending }, Passed = false };
        var ranked = new Score { Id = 2, UserId = 2, Beatmap = new Beatmap { Id = 5, Status = RankStatus.Ranked } };
        Assert.Equal(1, await service.SaveScoresAsync([unranked, unranked, ranked, new Score()], default));
        Assert.Equal(0, await service.SaveScoresAsync([unranked, ranked], default));
        var row = await db.ScoreHistories.SingleAsync();
        Assert.Equal(4, row.BeatmapId);
        Assert.Equal(3, row.GameMode);
        var restored = HistoryService.RestoreScore(row.Payload);
        Assert.False(restored.Passed);
        Assert.Equal(unranked.EndedAt, restored.EndedAt);
        Assert.Equal(RankStatus.Pending, restored.Beatmap!.Status);
    }

    [Fact]
    public async Task Collector_deduplicates_bindings_and_collects_active_modes_in_pages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var date = new DateOnly(2026, 9, 30);
        db.Users.AddRange(new UserModel { OsuUid = "2", Platform = "qq", PlatformUid = "1" },
            new UserModel { OsuUid = "2", Platform = "discord", PlatformUid = "1" });
        db.UserOsuInfoHistories.Add(new UserOsuInfoHistory { OsuUid = "2", GameMode = 0, Date = date.AddDays(-1), PlayCount = 10 });
        await db.SaveChangesAsync();
        var userCalls = 0;
        var offsets = new List<int>();
        var api = new RecordingOsuApiService
        {
            UserHandler = (_, _) => { userCalls++; return new User { Statistics = new UserStatistics { PlayCount = 11 } }; },
            ScoresHandler = (_, type, mode, limit, offset, fails, legacy) =>
            {
                Assert.Equal(ScoreType.Recent, type);
                Assert.Equal(GameMode.Osu, mode);
                Assert.True(fails);
                Assert.False(legacy);
                offsets.Add(offset);
                return Enumerable.Range(offset, offset == 0 ? limit : 1).Select(i => new Score
                {
                    Id = i + 1, UserId = 2, RulesetId = 0, Beatmap = new Beatmap { Id = 4, Status = RankStatus.Pending },
                    EndedAt = DateTimeOffset.UtcNow
                }).ToList();
            }
        };
        var options = Options.Create(new HistoryOptions { RecentLimit = 200 });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var history = new HistoryService(db, api, new Factory(HttpStatusCode.NotFound, ""), cache, options, NullLogger<HistoryService>.Instance);
        var collector = new HistoryCollector(db, api, history, options, NullLogger<HistoryCollector>.Instance);
        Assert.Equal(0, await collector.RunAsync("info", date, default));
        Assert.Equal(4, userCalls);
        Assert.Equal(0, await collector.RunAsync("info", date, default));
        Assert.Equal(4, userCalls);
        Assert.Equal(0, await collector.RunAsync("scores", date, default));
        Assert.Equal(new[] { 0, 100 }, offsets);
        Assert.Equal(101, await db.ScoreHistories.CountAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task External_history_merges_caches_and_falls_back(HttpStatusCode status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var options = new HistoryOptions();
        var today = options.Today(DateTimeOffset.UtcNow);
        db.UserOsuInfoHistories.Add(new UserOsuInfoHistory { OsuUid = "2", GameMode = 0, Date = today, Pp = 500, GlobalRank = 100 });
        await db.SaveChangesAsync();
        var body = $$"""
            [{"timestamp":"{{today:yyyy-MM-dd}}T00:00:00Z","pp_raw":"100","pp_rank":"500"},
             {"timestamp":"{{today.AddDays(-1):yyyy-MM-dd}}T00:00:00Z","pp_raw":90,"pp_rank":600},
             {"timestamp":"bad","pp_raw":1,"pp_rank":1}]
            """;
        var factory = new Factory(status, body);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new HistoryService(db, new RecordingOsuApiService(), factory, cache,
            Options.Create(options), NullLogger<HistoryService>.Instance);
        var points = await service.GetUserHistoryAsync("2", 0, 7, default);
        Assert.Equal(status == HttpStatusCode.OK ? 2 : 1, points.Count);
        Assert.Equal(500, points[^1].Pp);
        Assert.Equal("local", points[^1].Source);
        await service.GetUserHistoryAsync("2", 0, 7, default);
        Assert.Equal(status == HttpStatusCode.InternalServerError ? 2 : 1, factory.Calls);
    }
}
