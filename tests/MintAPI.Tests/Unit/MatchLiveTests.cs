using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintAPI.Services.MatchLive;
using MintAPI.Tests.TestDoubles;
using MintOsuApi;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Tests.Unit;

public class MatchLiveTests
{
    public sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-04T08:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance() => Now = Now.AddSeconds(20);
    }
    public sealed class Store : IMatchLiveStore
    {
        public Dictionary<int, string> Data { get; } = [];
        public Task<IReadOnlyList<LiveRoom>> LoadAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LiveRoom>>(
            Data.Values.Select(s => JsonConvert.DeserializeObject<LiveRoom>(s, OsuClient.BuildJsonSettings())!).ToList());
        public Task SaveAsync(LiveRoom room, CancellationToken ct)
        {
            Data[room.MatchId] = JsonConvert.SerializeObject(room, OsuClient.BuildJsonSettings());
            return Task.CompletedTask;
        }
        public Task DeleteAsync(int matchId, CancellationToken ct) { Data.Remove(matchId); return Task.CompletedTask; }
    }
    public static MatchLiveService Service(RecordingOsuApiService api, Store store, Clock clock, bool mock = false,
        int maxUpdates = 1000) => new(api, store, Options.Create(new MatchLiveOptions { MockEnabled = mock, MockAutoAdvanceSeconds = 0, MaxUpdates = maxUpdates }),
            clock, NullLogger<MatchLiveService>.Instance);

    [Fact]
    public async Task Mock_replays_real_game_with_same_event_id_and_persists_reconnect_cursor()
    {
        var clock = new Clock(); var store = new Store(); var api = new RecordingOsuApiService();
        var live = Service(api, store, clock, true);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "bot:qq:1");
        Assert.Equal("waiting", sub.Snapshot.Status); Assert.True(sub.Snapshot.IsMock);
        Assert.Equal(sub.SubscriptionId, (await live.SubscribeAsync(MatchLiveMock.MatchId, "bot:qq:1")).SubscriptionId);
        await live.SubscribeAsync(MatchLiveMock.MatchId, "bot:qq:2");
        var start = await live.AdvanceMockAsync(); Assert.Equal("playing", start.Status);
        var started = await live.UpdatesAsync(sub.SubscriptionId, sub.Cursor);
        var eventId = Assert.Single(started.Updates).EventId;
        Assert.Empty(started.Updates[0].Event!.Game!.Scores);
        // Recreating the coordinator restores subscriptions and an unresolved game from the durable store.
        live = Service(api, store, clock, true);
        await live.AdvanceMockAsync();
        var finished = await live.UpdatesAsync(sub.SubscriptionId, started.Cursor);
        var update = Assert.Single(finished.Updates);
        Assert.Equal(eventId, update.EventId); Assert.Equal("game-finished", update.Type);
        Assert.Equal(6, update.Event!.Game!.Scores.Count);
        Assert.All(update.Event.Game.Scores, score => Assert.True(score.Score > 0));
        Assert.Equal("waiting", finished.Snapshot.Status);
        await live.AdvanceMockAsync();
        var closed = await live.UpdatesAsync(sub.SubscriptionId, finished.Cursor);
        Assert.Equal("room-closed", Assert.Single(closed.Updates).Type);
        Assert.Equal("closed", closed.Snapshot.Status);
        await live.AdvanceMockAsync(); await live.TickAsync();
        Assert.Empty((await live.UpdatesAsync(sub.SubscriptionId, closed.Cursor)).Updates);
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Shared_poll_rewinds_pending_game_and_does_not_repeat_finished_notifications()
    {
        var clock = new Clock(); var store = new Store();
        var calls = new List<long>();
        var api = new RecordingOsuApiService { MatchHandler = _ => MatchLiveMock.Build(1, clock.Now) };
        var live = Service(api, store, clock);
        var a = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        var b = await live.SubscribeAsync(MatchLiveMock.MatchId, "b");
        var room = await live.GetRoomAsync(MatchLiveMock.MatchId);
        var pendingId = room.Match.EventList.Last(e => e.Game is not null).Id;
        api.MatchAfterHandler = (_, after) =>
        {
            calls.Add(after);
            return MatchLiveMock.Build(2, DateTimeOffset.Parse("2026-10-04T08:00:00Z"));
        };
        clock.Advance(); await live.TickAsync();
        Assert.Equal(new[] { pendingId - 1 }, calls);
        var update = await live.UpdatesAsync(a.SubscriptionId, a.Cursor);
        Assert.Equal("game-finished", Assert.Single(update.Updates).Type);
        Assert.Single((await live.UpdatesAsync(b.SubscriptionId, b.Cursor)).Updates);
        clock.Advance(); await live.TickAsync();
        Assert.Empty((await live.UpdatesAsync(a.SubscriptionId, update.Cursor)).Updates);
        Assert.Equal(pendingId, calls[1]);
        Assert.Equal(3, api.CallCount); // Initial snapshot and two shared polls, independent of subscribers.
    }

    [Fact]
    public async Task Errors_keep_state_backoff_then_recover_and_aborted_games_have_no_fake_scores()
    {
        var clock = new Clock(); var store = new Store();
        var api = new RecordingOsuApiService { MatchHandler = _ => MatchLiveMock.Build(1, clock.Now),
            MatchAfterHandler = (_, _) => throw new HttpRequestException("offline") };
        var live = Service(api, store, clock);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        clock.Advance(); await live.TickAsync();
        var error = await live.UpdatesAsync(sub.SubscriptionId, sub.Cursor);
        Assert.Equal("playing", error.Snapshot.Status); Assert.NotNull(error.Snapshot.Error);
        Assert.Equal("polling-error", Assert.Single(error.Updates).Type);
        var calls = api.CallCount; await live.TickAsync(); Assert.Equal(calls, api.CallCount);
        api.MatchAfterHandler = (_, _) =>
        {
            var result = MatchLiveMock.Build(2, DateTimeOffset.Parse("2026-10-04T08:00:00Z"));
            result.EventList.Last(e => e.Game is not null).Game!.Scores = [];
            return result;
        };
        clock.Advance(); await live.TickAsync();
        var recovered = await live.UpdatesAsync(sub.SubscriptionId, error.Cursor);
        Assert.Equal(new[] { "game-aborted", "recovered" }, recovered.Updates.Select(u => u.Type));
        Assert.Null(recovered.Snapshot.Error);
    }

    [Fact]
    public async Task Lease_expiration_stops_polling_and_stale_revision_requires_new_snapshot()
    {
        var clock = new Clock(); var store = new Store(); var api = new RecordingOsuApiService();
        var live = Service(api, store, clock, true, 1);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        await live.AdvanceMockAsync(); await live.AdvanceMockAsync();
        await Assert.ThrowsAsync<LiveGoneException>(() => live.UpdatesAsync(sub.SubscriptionId, 0));
        clock.Now = clock.Now.AddMinutes(31);
        await Assert.ThrowsAsync<LiveGoneException>(() => live.UpdatesAsync(sub.SubscriptionId, 2));
        await live.TickAsync();
        Assert.Equal(0, api.CallCount);
    }
    [Fact]
    public async Task Mock_advances_automatically_on_timer_and_reset_retains_subscription()
    {
        var clock = new Clock(); var store = new Store();
        var live = new MatchLiveService(new RecordingOsuApiService(), store,
            Options.Create(new MatchLiveOptions { MockEnabled = true, MockAutoAdvanceSeconds = 10 }), clock,
            NullLogger<MatchLiveService>.Instance);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        foreach (var state in new[] { "playing", "waiting", "closed" })
        {
            clock.Advance(); await live.TickAsync();
            Assert.Equal(state, (await live.UpdatesAsync(sub.SubscriptionId, 0)).Snapshot.Status);
        }
        var before = (await live.UpdatesAsync(sub.SubscriptionId, 0)).Cursor;
        await live.ResetMockAsync();
        var reset = await live.UpdatesAsync(sub.SubscriptionId, before);
        Assert.Equal("mock-reset", Assert.Single(reset.Updates).Type);
        Assert.Equal("waiting", reset.Snapshot.Status);
    }

    [Fact]
    public async Task Unsubscribing_releases_active_match_capacity_and_expired_rooms_are_deleted()
    {
        var clock = new Clock(); var store = new Store();
        var api = new RecordingOsuApiService { MatchHandler = _ => MatchLiveMock.Build(0, clock.Now) };
        var live = new MatchLiveService(api, store, Options.Create(new MatchLiveOptions { MaxMatches = 1 }),
            clock, NullLogger<MatchLiveService>.Instance);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        await Assert.ThrowsAsync<LiveConflictException>(() => live.SubscribeAsync(42, "b"));
        await live.UnsubscribeAsync(sub.SubscriptionId);
        api.MatchHandler = _ => { var match = MatchLiveMock.Build(0, clock.Now); match.MatchInfo.Id = 42; return match; };
        await live.SubscribeAsync(42, "b");
        clock.Now = clock.Now.AddMinutes(61); await live.TickAsync();
        Assert.Empty(store.Data);
    }

    [Fact]
    public async Task Initial_history_is_backfilled_once_and_nonadvancing_pages_are_rejected()
    {
        var clock = new Clock(); var store = new Store();
        var complete = MatchLiveMock.Build(2, clock.Now);
        var beforeCalls = new List<long?>();
        var api = new RecordingOsuApiService { MatchHandler = before =>
        {
            beforeCalls.Add(before);
            var result = MatchLiveMock.Build(2, clock.Now);
            result.EventList = before.HasValue ? result.EventList.Take(1).ToList() : result.EventList.Skip(1).ToList();
            return result;
        } };
        var live = Service(api, store, clock);
        await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        await live.SubscribeAsync(MatchLiveMock.MatchId, "b");
        Assert.Equal(new long?[] { null, complete.EventList.Last().Id }, beforeCalls);
        Assert.Equal(2, (await live.GetRoomAsync(MatchLiveMock.MatchId)).Match.EventList.Count);
        var badApi = new RecordingOsuApiService { MatchHandler = _ =>
        {
            var result = MatchLiveMock.Build(2, clock.Now);
            result.EventList = result.EventList.Skip(1).ToList();
            return result;
        } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(badApi, new Store(), clock)
            .SubscribeAsync(MatchLiveMock.MatchId, "c"));
    }

    [Fact]
    public async Task Live_query_budget_does_not_grow_with_subscriber_count()
    {
        var clock = new Clock(); var store = new Store();
        var api = new RecordingOsuApiService { MatchHandler = _ => MatchLiveMock.Build(1, clock.Now) };
        var live = new MatchLiveService(api, store,
            Options.Create(new MatchLiveOptions { RequestBudgetPerMinute = 1, MaxSubscriptionsPerScope = 1 }), clock,
            NullLogger<MatchLiveService>.Instance);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        await live.SubscribeAsync(MatchLiveMock.MatchId, "b");
        Assert.Equal(1, api.CallCount);
        clock.Advance(); await live.TickAsync();
        Assert.Equal(1, api.CallCount);
        Assert.NotNull((await live.UpdatesAsync(sub.SubscriptionId, 0)).Snapshot.Error);
    }

    [Fact]
    public async Task Scope_cannot_exceed_subscription_limit_and_failed_registration_does_not_create_subscription()
    {
        var clock = new Clock(); var store = new Store();
        var api = new RecordingOsuApiService { MatchHandler = _ =>
        {
            var match = MatchLiveMock.Build(0, clock.Now); match.MatchInfo.Id = 42; return match;
        } };
        var live = new MatchLiveService(api, store,
            Options.Create(new MatchLiveOptions { MockEnabled = true, MaxSubscriptionsPerScope = 1 }), clock,
            NullLogger<MatchLiveService>.Instance);
        var sub = await live.SubscribeAsync(MatchLiveMock.MatchId, "a");
        await Assert.ThrowsAsync<LiveConflictException>(() => live.SubscribeAsync(42, "a"));
        Assert.Equal(sub.SubscriptionId, (await live.SubscribeAsync(MatchLiveMock.MatchId, "a")).SubscriptionId);
        Assert.Empty((await live.GetRoomAsync(42)).Subscriptions);
    }

}
