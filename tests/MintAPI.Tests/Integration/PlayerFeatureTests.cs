using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Middleware;
using MintAPI.Models.Entities;
using MintAPI.OpenApi;
using MintAPI.Rendering.PlayerFeatures;
using MintAPI.Services;
using MintAPI.Services.PlayerFeatures;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Tests.Integration;

public sealed class PlayerFeatureTests
{
    internal sealed class Fixture : IAsyncDisposable
    {
        public readonly SqliteConnection Connection = new("Data Source=:memory:");
        public AppDbContext Db = null!;
        public readonly RecordingOsuApiService Api = new();
        public readonly IOptions<HistoryOptions> HistoryOptions = Options.Create(new HistoryOptions());
        public readonly IOptions<PlayerWatchOptions> WatchOptions = Options.Create(new PlayerWatchOptions());
        public PlayerReportService Reports = null!;
        public PlayerWatchService Watches = null!;
        public static User User(int id = 42, double pp = 6000, int rank = 26000) => new()
        { Id = id, Username = "Mint <玩家> " + id, CountryCode = "CN", Statistics = new() { Pp = pp, GlobalRank = rank } };
        public static Score Score(long id = 1, int mode = 0) => new()
        {
            Id = id, UserId = 42, RulesetId = mode, Pp = 356.42, Accuracy = .9872, MaxCombo = 1842,
            Rank = Grade.S, Passed = true, LegacyScoreId = id, EndedAt = DateTimeOffset.UtcNow,
            TotalScore = 999000, BeatmapId = 657917, Statistics = new() { Great = 2358, Ok = 26, Meh = 15, Miss = 1, Perfect = 300, Good = 20 },
            Beatmap = new() { Id = 657917, BeatmapsetId = 292301, Version = "Four Dimensions", Status = RankStatus.Ranked },
            Beatmapset = new() { Id = 292301, Title = "Blue Zenith <test>", Artist = "xi", Creator = "Asphyxia" }
        };
        public async Task InitAsync()
        {
            await Connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
            await Db.Database.EnsureCreatedAsync();
            Api.UserHandler = (id, _) => User(int.TryParse(id, out var uid) ? uid : 42);
            Api.ScoresHandler = (_, _, _, _, _, _, _) => [Score()];
            Api.BeatmapHandler = _ => Score().Beatmap!;
            Api.BeatmapUserScoresHandler = (_, _, mode) => [Score(mode: (int)(mode ?? GameMode.Osu))];
            var history = new HistoryService(Db, Api, null!, null!, HistoryOptions, NullLogger<HistoryService>.Instance);
            Reports = new(Db, Api, history, HistoryOptions);
            Watches = new(Db, Api, Reports, WatchOptions, HistoryOptions);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    [Fact]
    public async Task Weekly_uses_period_end_snapshot_keeps_positions_and_does_not_substitute_live_BP()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var start = new DateOnly(2026, 10, 5); var end = start.AddDays(7);
        f.Db.UserOsuInfoHistories.AddRange(new UserOsuInfoHistory { OsuUserId = 42, Date = start, Pp = 5900 },
            new UserOsuInfoHistory { OsuUserId = 42, Date = start.AddDays(2), Pp = 5950 });
        var before = Fixture.Score(1); before.EndedAt = new DateTimeOffset(2026, 10, 4, 15, 59, 0, TimeSpan.Zero);
        var within = Fixture.Score(2); within.EndedAt = new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero);
        var after = Fixture.Score(3); after.EndedAt = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.Zero);
        f.Db.BestPlaySnapshots.Add(new() { UserId = 42, Date = end, ObservedAt = DateTimeOffset.UtcNow,
            Payload = JsonConvert.SerializeObject(new[] { before, within, after }) });
        await f.Db.SaveChangesAsync();
        var report = await f.Reports.WeeklyAsync("42", 0, start, default);
        Assert.True(report.BestCoverage); Assert.Null(report.Final); Assert.NotNull(report.Baseline);
        Assert.Equal(2, Assert.Single(report.NewBestPlays).Position);
        Assert.Equal(1, f.Api.CallCount); // Identity only, no live BP fetch for a historical report.
        var partial = await f.Reports.WeeklyAsync("42", 0, start.AddDays(-7), default);
        Assert.False(partial.BestCoverage); Assert.Empty(partial.NewBestPlays);
    }

    [Fact]
    public async Task Daily_BP_capture_is_idempotent_and_failure_does_not_save_empty_snapshot()
    {
        await using var f = new Fixture(); await f.InitAsync();
        await f.Reports.CaptureBestAsync(42, 0, new(2026, 10, 12), default);
        await f.Reports.CaptureBestAsync(42, 0, new(2026, 10, 12), default);
        Assert.Equal(1, f.Api.CallCount); Assert.Single(await f.Db.BestPlaySnapshots.ToListAsync());
        f.Api.ScoresHandler = (_, _, _, _, _, _, _) => throw new HttpRequestException("private-upstream");
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Reports.CaptureBestAsync(42, 0, new(2026, 10, 13), default));
        Assert.Single(await f.Db.BestPlaySnapshots.ToListAsync());
    }

    [Fact]
    public async Task Subscriptions_persist_baseline_deduplicate_replay_and_resume_after_restart()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var subscription = await f.Watches.SubscribeAsync("42", 0, "qq:group:1", default);
        var same = await f.Watches.SubscribeAsync("42", 0, "qq:group:1", default);
        Assert.Equal(subscription.Id, same.Id);
        Assert.Empty((await f.Watches.UpdatesAsync(subscription.Id, subscription.Cursor, default)).Updates);
        var watch = await f.Db.PlayerWatches.SingleAsync();
        watch.CheckedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await f.Db.SaveChangesAsync();
        f.Api.UserHandler = (_, _) => Fixture.User(pp: 7020, rank: 24000);
        f.Api.ScoresHandler = (_, _, _, _, _, _, _) => [Fixture.Score(2), Fixture.Score(1)];
        await f.Watches.PollAsync(42, 0, default);
        var updates = await f.Watches.UpdatesAsync(subscription.Id, subscription.Cursor, default);
        var item = Assert.Single(updates.Updates);
        Assert.Contains("新 BP", item.Text); Assert.Contains("BP #1", item.Text);
        Assert.Contains("25,000", item.Text); Assert.Contains("7,000", item.Text);
        Assert.Contains("观测差值", item.Text);
        // Replay is intentional until the consumer stores cursor after successful delivery.
        Assert.Single((await f.Watches.UpdatesAsync(subscription.Id, subscription.Cursor, default)).Updates);
        Assert.Empty((await f.Watches.UpdatesAsync(subscription.Id, updates.Cursor, default)).Updates);
        await using var restarted = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Connection).Options);
        var history = new HistoryService(restarted, f.Api, null!, null!, f.HistoryOptions, NullLogger<HistoryService>.Instance);
        var reports = new PlayerReportService(restarted, f.Api, history, f.HistoryOptions);
        var service = new PlayerWatchService(restarted, f.Api, reports, f.WatchOptions, f.HistoryOptions);
        var state = await restarted.PlayerWatches.SingleAsync(); state.CheckedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await restarted.SaveChangesAsync();
        await service.PollAsync(42, 0, default);
        Assert.Empty((await service.UpdatesAsync(subscription.Id, updates.Cursor, default)).Updates);
        Assert.Single(await restarted.PlayerWatchEvents.ToListAsync());
    }

    [Fact]
    public async Task Poll_failure_preserves_baseline_and_expired_or_future_cursor_has_distinct_error()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var subscription = await f.Watches.SubscribeAsync("42", 0, "test", default);
        var state = await f.Db.PlayerWatches.SingleAsync(); state.CheckedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await f.Db.SaveChangesAsync(); var payload = state.Payload;
        f.Api.ScoresHandler = (_, _, _, _, _, _, _) => throw new OsuQuotaExceededException();
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => f.Watches.PollAsync(42, 0, default));
        Assert.Equal(payload, state.Payload); Assert.Equal(subscription.Cursor, state.Revision);
        var future = await Assert.ThrowsAsync<ApiLookupException>(() => f.Watches.UpdatesAsync(subscription.Id, 1000, default));
        Assert.Equal(ErrorCatalog.InvalidArgument, future.Definition);
        var expired = await Assert.ThrowsAsync<ApiLookupException>(() => f.Watches.UpdatesAsync(subscription.Id, 0, default));
        Assert.Equal(ErrorCatalog.ResourceExpired, expired.Definition);
    }

    [Fact]
    public void Recalculated_old_BP_and_unknown_rank_do_not_generate_false_events()
    {
        var old = Fixture.Score(); old.EndedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var user = Fixture.User(); user.Statistics!.GlobalRank = null;
        Assert.Empty(PlayerWatchService.BuildText(new(user, []), new(Fixture.User(rank: 1), [old]),
            DateTimeOffset.UtcNow.AddMinutes(-10), 0, TimeZoneInfo.Utc));
    }

    [Fact]
    public async Task Comparison_filters_mods_and_disables_highlight_for_different_settings_or_clients()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var a = Fixture.Score(); a.Mods = [new() { Acronym = "DT", Settings = new() { ["speed_change"] = 1.2 } }];
        var b = Fixture.Score(2); b.Mods = [new() { Acronym = "DT", Settings = new() { ["speed_change"] = 1.5 } }];
        f.Api.BeatmapUserScoresHandler = (_, uid, _) => [uid == 42 ? a : b];
        Assert.False((await f.Reports.CompareAsync("42", "43", 657917, 0, "DT", true, default)).Comparable);
        b.Mods = a.Mods; b.LegacyScoreId = null;
        Assert.False((await f.Reports.CompareAsync("42", "43", 657917, 0, "DT", null, default)).Comparable);
        var error = await Assert.ThrowsAsync<ApiLookupException>(() => f.Reports.CompareAsync("42", "43", 657917, 0, "HR", null, default));
        Assert.Equal(ErrorCatalog.ScoreNotFound, error.Definition);
        f.Api.BeatmapHandler = _ => throw new HttpRequestException("network failure");
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Reports.CompareAsync("42", "43", 657917, 0, null, null, default));
    }

    [Theory]
    [InlineData("/user_info/weekly?platform=qq&platform_uid=missing", "USER_NOT_BOUND", 404)]
    [InlineData("/user_info/weekly?platform=qq&platform_uid=123", "HISTORY_NOT_FOUND", 404)]
    [InlineData("/user_info/weekly?platform=qq&platform_uid=missing&week_start=2099-01-01", "INVALID_ARGUMENT", 400)]
    [InlineData("/score/compare?left_user=42&right_user=43&beatmap_id=0", "INVALID_ARGUMENT", 400)]
    [InlineData("/score/compare?left_user=42&right_user=43&beatmap_id=1&game_mode=bad", "INVALID_ARGUMENT", 400)]
    [InlineData("/score/compare?left_user=42&right_user=43&beatmap_id=1&mods=", "INVALID_ARGUMENT", 400)]
    [InlineData("/players/watch/subscriptions/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/updates?after=0", "RESOURCE_EXPIRED", 410)]
    public async Task New_endpoints_keep_problem_json_for_png_accept(string path, string code, int status)
    {
        await using var f = new Fixture(); await f.InitAsync();
        f.Db.Users.Add(new() { Platform = "qq", PlatformUid = "123", OsuUserId = 42 });
        await f.Db.SaveChangesAsync();
        await using var app = await AppAsync(f, new CaptureRenderer());
        using var http = Http(app); http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        using var response = await http.GetAsync(path);
        await ApiErrorAssertions.AssertAsync(response, new(status, code, ""));
    }

    [Theory]
    [InlineData("upstream", "OSU_API_UNAVAILABLE", 502)]
    [InlineData("missing-map", "BEATMAP_NOT_FOUND", 404)]
    [InlineData("missing-score", "SCORE_NOT_FOUND", 404)]
    [InlineData("uncollected", "LOCAL_SCORE_NOT_COLLECTED", 404)]
    [InlineData("missing-user", "OSU_USER_NOT_FOUND", 404)]
    [InlineData("render", "RENDER_FAILED", 500)]
    [InlineData("busy", "SERVICE_BUSY", 503)]
    public async Task Dependency_failures_keep_http_contract(string failure, string code, int status)
    {
        await using var f = new Fixture(); await f.InitAsync();
        var capture = new CaptureRenderer { Failure = failure == "render" ? new Exception("private-render") : null };
        if (failure == "upstream") f.Api.UserHandler = (_, _) => throw new HttpRequestException("private-upstream");
        if (failure == "uncollected") f.Api.BeatmapHandler = _ => new() { Id = 657917, Status = RankStatus.Pending };
        if (failure == "missing-user") f.Api.UserHandler = (_, _) => throw new HttpRequestException("private-user", null, HttpStatusCode.NotFound);
        if (failure == "busy") f.Api.UserHandler = (_, _) => throw new OsuQuotaExceededException();
        if (failure == "missing-map") f.Api.BeatmapHandler = _ => throw new HttpRequestException("private-map", null, HttpStatusCode.NotFound);
        if (failure == "missing-score") f.Api.BeatmapUserScoresHandler = (_, _, _) => [];
        await using var app = await AppAsync(f, capture);
        using var http = Http(app); http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        using var response = await http.GetAsync("/score/compare?left_user=42&right_user=43&beatmap_id=657917");
        await ApiErrorAssertions.AssertAsync(response, new(status, code, ""), "private-");
        if (failure == "busy") Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Text_updates_and_cursor_headers_are_ready_for_bot_consumption()
    {
        await using var f = new Fixture(); await f.InitAsync();
        await using var app = await AppAsync(f, new CaptureRenderer()); using var http = Http(app);
        using var created = await http.PostAsJsonAsync("/players/watch/subscriptions", new { user = "42", scope = "qq:test", gameMode = 0 });
        created.EnsureSuccessStatusCode();
        var sub = await created.Content.ReadFromJsonAsync<SubscriptionResult>();
        f.Db.PlayerWatchEvents.Add(new() { UserId = 42, Cursor = sub!.Cursor + 1, Text = "【新 BP】测试文本", ObservedAt = DateTimeOffset.UtcNow });
        (await f.Db.PlayerWatches.SingleAsync()).Revision++;
        await f.Db.SaveChangesAsync();
        using var response = await http.GetAsync($"/players/watch/subscriptions/{sub.Id}/updates?after={sub.Cursor}&format=text");
        response.EnsureSuccessStatusCode(); Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("【新 BP】测试文本", await response.Content.ReadAsStringAsync());
        Assert.Equal((sub.Cursor + 1).ToString(), response.Headers.GetValues("X-PlayerWatch-Cursor").Single());
        using var deleted = await http.DeleteAsync($"/players/watch/subscriptions/{sub.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Pagination_never_skips_events_and_retention_rejects_stale_cursor()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var sub = await f.Watches.SubscribeAsync("42", 0, "test", default);
        f.Db.PlayerWatchEvents.AddRange(Enumerable.Range(1, 101).Select(i => new PlayerWatchEvent
        { UserId = 42, Cursor = sub.Cursor + i, ObservedAt = DateTimeOffset.UtcNow, Text = "event " + i }));
        var watch = await f.Db.PlayerWatches.SingleAsync(); watch.Revision = sub.Cursor + 101;
        await f.Db.SaveChangesAsync();
        var first = await f.Watches.UpdatesAsync(sub.Id, sub.Cursor, default);
        Assert.True(first.HasMore); Assert.Equal(100, first.Updates.Count);
        var second = await f.Watches.UpdatesAsync(sub.Id, first.Cursor, default);
        Assert.False(second.HasMore); Assert.Single(second.Updates); Assert.Equal(watch.Revision, second.Cursor);
        var old = await f.Db.PlayerWatchEvents.OrderBy(x => x.Cursor).FirstAsync(); old.ObservedAt = DateTimeOffset.UtcNow.AddDays(-8);
        watch.CheckedAt = DateTimeOffset.UtcNow.AddMinutes(-10); await f.Db.SaveChangesAsync();
        await f.Watches.PollAsync(42, 0, default);
        var expired = await Assert.ThrowsAsync<ApiLookupException>(() => f.Watches.UpdatesAsync(sub.Id, sub.Cursor, default));
        Assert.Equal(ErrorCatalog.ResourceExpired, expired.Definition);
        await f.Watches.UnsubscribeAsync(sub.Id, default);
        Assert.Empty(await f.Db.PlayerSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Both_image_routes_render_successful_HTTP_responses_through_Chromium()
    {
        await using var f = new Fixture(); await f.InitAsync();
        var today = f.HistoryOptions.Value.Today(DateTimeOffset.UtcNow);
        var end = today.AddDays(-((int)today.DayOfWeek + 6) % 7); var start = end.AddDays(-7);
        f.Db.Users.Add(new() { Platform = "qq", PlatformUid = "123", OsuUserId = 42 });
        f.Db.UserOsuInfoHistories.AddRange(Enumerable.Range(0, 8).Select(i => new UserOsuInfoHistory
        { OsuUserId = 42, Date = start.AddDays(i), Pp = 6000 + i, PlayCount = 100 + i, GlobalRank = 26000 - i }));
        var score = Fixture.Score(); score.EndedAt = new DateTimeOffset(start.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        f.Db.BestPlaySnapshots.Add(new() { UserId = 42, Date = end, ObservedAt = DateTimeOffset.UtcNow, Payload = JsonConvert.SerializeObject(new[] { score }) });
        await f.Db.SaveChangesAsync();
        await using var browser = new PlaywrightBrowserProvider(NullLogger<PlaywrightBrowserProvider>.Instance);
        await browser.StartAsync(); await using var app = await AppAsync(f, new PlaywrightRenderer(browser));
        using var http = Http(app); http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        foreach (var path in new[] { "/user_info/weekly?platform=qq&platform_uid=123", "/score/compare?left_user=42&right_user=43&beatmap_id=657917" })
        {
            using var response = await http.GetAsync(path); response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync(); Assert.True(bytes.Length > 1000);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, bytes[..4]);
        }
    }

    [Fact]
    public async Task OpenApi_exposes_new_routes_and_response_types()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(PlayerFeaturesController).Assembly);
        builder.Services.AddOpenApi(x => x.AddXmlComments(typeof(PlayerFeaturesController).Assembly));
        await using var app = builder.Build(); app.MapControllers(); app.MapOpenApi();
        app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
        using var http = Http(app);
        using var response = await http.GetAsync("/openapi/v1.json");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        using var json = System.Text.Json.JsonDocument.Parse(text);
        foreach (var path in new[] { "/user_info/weekly", "/score/compare", "/players/watch/subscriptions", "/players/watch/subscriptions/{id}/updates" })
            Assert.True(json.RootElement.GetProperty("paths").TryGetProperty(path, out _));
        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/user_info/weekly", "/score/compare" })
        {
            var responses = paths.GetProperty(path).GetProperty("get").GetProperty("responses");
            Assert.True(responses.GetProperty("200").GetProperty("content").TryGetProperty("image/png", out _));
            Assert.True(responses.GetProperty("404").GetProperty("content").TryGetProperty("application/problem+json", out _));
            Assert.True(responses.TryGetProperty("502", out _));
        }
    }

    internal static async Task<WebApplication> AppAsync(Fixture f, IRenderService render)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddControllers(x => x.Filters.Add<ApiErrorFilter>()).AddApplicationPart(typeof(PlayerFeaturesController).Assembly);
        builder.Services.AddExceptionHandler<RetryableExceptionHandler>(); builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(f.Db); builder.Services.AddSingleton(f.Reports); builder.Services.AddSingleton(f.Watches);
        builder.Services.AddSingleton(f.HistoryOptions); builder.Services.AddSingleton(f.WatchOptions);
        builder.Services.AddSingleton(new PlayerFeatureRenderer(render, new Images(), new Covers(), NullLogger<PlayerFeatureRenderer>.Instance));
        var app = builder.Build(); app.UseExceptionHandler(); app.MapControllers(); app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(); return app;
    }
    internal static HttpClient Http(WebApplication app) => new() { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
    internal sealed class CaptureRenderer : IRenderService
    {
        public string Html = ""; public Exception? Failure;
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        { Html = html; if (Failure != null) throw Failure; return Task.FromResult(new byte[] { 0x89, 0x50, 0x4e, 0x47 }); }
    }
    internal sealed class Images : IImageCacheService
    {
        private static readonly byte[] Pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZsAAAAASUVORK5CYII=");
        private static byte[]? Resource(string name) => Environment.GetEnvironmentVariable(name) is string path ? File.ReadAllBytes(path) : null;
        public Task<byte[]> GetAvatarAsync(string url, int uid) => Task.FromResult(Resource("PLAYER_FEATURE_AVATAR") ?? Pixel);
        public Task<byte[]?> GetUserBannerAsync(string? url, int uid) => Task.FromResult(Resource("PLAYER_FEATURE_BANNER"));
        public Task<byte[]?> GetUserBackgroundAsync(int uid) => Task.FromResult<byte[]?>(null);
        public Task SaveUserBackgroundAsync(int uid, byte[] data) => throw new NotSupportedException();
        public Task<byte[]> GetBadgeAsync(string url, int uid, int index) => throw new NotSupportedException();
    }
    internal sealed class Covers : IBeatmapFileService
    {
        public Task<byte[]?> GetListCoverAsync(int id, CancellationToken ct = default) => Task.FromResult(Environment.GetEnvironmentVariable("PLAYER_FEATURE_BACKGROUND") is string path ? File.ReadAllBytes(path) : null);
        public Task<string> GetOsuFilePathAsync(int a, int b) => throw new NotSupportedException();
        public Task<byte[]> GetMapBgAsync(int a, int b, string? c = null) => throw new NotSupportedException();
        public string GetBgFilename(string path) => throw new NotSupportedException();
    }
}
