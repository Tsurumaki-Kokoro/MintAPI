using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using MintOsuApi;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Services.MatchLive;

public sealed class MatchLiveService(IOsuApiService api, IMatchLiveStore store, IOptions<MatchLiveOptions> options,
    TimeProvider clock, ILogger<MatchLiveService> logger) : IDisposable
{
    private sealed class Entry(LiveRoom room)
    {
        public LiveRoom Room { get; } = room;
        public SemaphoreSlim Gate { get; } = new(1);
    }
    private readonly SlidingWindowRateLimiter _budget = new(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = options.Value.RequestBudgetPerMinute, Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6, QueueLimit = 0
    });
    private readonly ConcurrentDictionary<int, Entry> _rooms = new();
    private readonly SemaphoreSlim _restoreGate = new(1);
    private readonly object _registry = new();
    private bool _restored;
    private MatchLiveOptions Settings => options.Value;
    private DateTimeOffset Now => clock.GetUtcNow();
    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value,
        OsuClient.BuildJsonSettings()), OsuClient.BuildJsonSettings())!;

    private async Task RestoreAsync(CancellationToken ct)
    {
        await _restoreGate.WaitAsync(ct);
        try
        {
            if (_restored) return;
            foreach (var room in await store.LoadAsync(ct))
            {
                if (room.IsMock && !Settings.MockEnabled) continue;
                _rooms.TryAdd(room.MatchId, new Entry(room));
            }
            _restored = true;
        }
        finally { _restoreGate.Release(); }
    }

    public async Task<LiveSubscriptionResult> SubscribeAsync(int matchId, string scope, CancellationToken ct = default)
    {
        await RestoreAsync(ct);
        Entry entry;
        lock (_registry)
        {
            if (!_rooms.TryGetValue(matchId, out entry!))
            {
                if (_rooms.Values.Count(e => !e.Room.ClosedAt.HasValue && (!e.Room.Initialized || e.Room.Subscriptions.Values.Any(s => s.ExpiresAt > Now))) >= Settings.MaxMatches)
                    throw new LiveConflictException("已达到同时追踪比赛的上限。");
                entry = new Entry(new LiveRoom { MatchId = matchId, IsMock = Settings.MockEnabled && matchId == MatchLiveMock.MatchId });
                _rooms[matchId] = entry;
            }
        }
        await entry.Gate.WaitAsync(ct);
        try
        {
            lock (_registry)
                if (!_rooms.TryGetValue(matchId, out var current) || !ReferenceEquals(current, entry))
                    throw new LiveConflictException("比赛注册状态已变化，请重试订阅。");
            if (!entry.Room.Initialized)
            {
                var initial = entry.Room.IsMock ? MatchLiveMock.Build(0, Now)
                    : await LoadInitialAsync(matchId, ct);
                Merge(entry.Room, initial, initial: true);
                entry.Room.Initialized = true;
                entry.Room.MockNextAdvance = Now.AddSeconds(Settings.MockAutoAdvanceSeconds);
            }
            LiveSubscription subscription;
            lock (_registry)
            {
                var duplicate = entry.Room.Subscriptions.Values.FirstOrDefault(s => s.Scope == scope && s.ExpiresAt > Now);
                if (duplicate is not null) subscription = duplicate with { ExpiresAt = Now.AddMinutes(Settings.LeaseMinutes) };
                else
                {
                    var count = _rooms.Values.Sum(e => e.Room.Subscriptions.Values.Count(s => s.Scope == scope && s.ExpiresAt > Now));
                    if (count >= Settings.MaxSubscriptionsPerScope) throw new LiveConflictException("此 scope 已达到订阅上限。");
                    subscription = new LiveSubscription(Guid.NewGuid().ToString("N"), scope, Now.AddMinutes(Settings.LeaseMinutes));
                }
                entry.Room.Subscriptions[subscription.Id] = subscription;
            }
            await store.SaveAsync(entry.Room, ct);
            return new(subscription.Id, subscription.ExpiresAt, Snapshot(entry.Room), entry.Room.Revision);
        }
        catch
        {
            if (!entry.Room.Initialized) _rooms.TryRemove(new KeyValuePair<int, Entry>(matchId, entry));
            throw;
        }
        finally { entry.Gate.Release(); }
    }

    public async Task<LiveUpdatesResult> UpdatesAsync(string subscriptionId, long after, CancellationToken ct = default)
    {
        await RestoreAsync(ct);
        var entry = FindSubscription(subscriptionId);
        await entry.Gate.WaitAsync(ct);
        try
        {
            if (!entry.Room.Subscriptions.TryGetValue(subscriptionId, out var subscription) || subscription.ExpiresAt <= Now)
                throw new LiveGoneException("订阅已过期，请重新订阅并读取快照。");
            if (after > entry.Room.Revision) throw new ArgumentException("after 超过当前更新游标。");
            if (entry.Room.Updates.Count > 0 && after < entry.Room.Updates[0].Revision - 1)
                throw new LiveGoneException("更新游标已过期，请重新订阅并读取快照。");
            lock (_registry) entry.Room.Subscriptions[subscriptionId] = subscription with { ExpiresAt = Now.AddMinutes(Settings.LeaseMinutes) };
            await store.SaveAsync(entry.Room, ct);
            return new(Snapshot(entry.Room), entry.Room.Revision, Clone(entry.Room.Updates.Where(u => u.Revision > after).ToList()));
        }
        finally { entry.Gate.Release(); }
    }

    public async Task UnsubscribeAsync(string id, CancellationToken ct = default)
    {
        await RestoreAsync(ct);
        var entry = FindSubscription(id);
        await entry.Gate.WaitAsync(ct);
        try
        {
            lock (_registry) entry.Room.Subscriptions.Remove(id);
            await store.SaveAsync(entry.Room, ct);
        }
        finally { entry.Gate.Release(); }
    }

    private Entry FindSubscription(string id)
    {
        lock (_registry)
            return _rooms.Values.FirstOrDefault(e => e.Room.Subscriptions.ContainsKey(id))
                ?? throw new LiveGoneException("订阅不存在或已过期。");
    }

    public async Task<LiveRoom> GetRoomAsync(int matchId, CancellationToken ct = default)
    {
        await RestoreAsync(ct);
        if (!_rooms.TryGetValue(matchId, out var entry)) throw new KeyNotFoundException("该比赛未被追踪。");
        await entry.Gate.WaitAsync(ct);
        try { return Clone(entry.Room); }
        finally { entry.Gate.Release(); }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        await RestoreAsync(ct);
        // Each match has its own lock. No global lock is held while awaiting upstream requests.
        await Parallel.ForEachAsync(_rooms.Values, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (entry, _) =>
        {
            ct.ThrowIfCancellationRequested();
            if (!await entry.Gate.WaitAsync(0, ct)) return;
            try
            {
                var room = entry.Room;
                lock (_registry)
                {
                    foreach (var id in room.Subscriptions.Where(s => s.Value.ExpiresAt <= Now).Select(s => s.Key).ToArray())
                        room.Subscriptions.Remove(id);
                }
                var retainFrom = room.ClosedAt ?? room.LastSuccess ?? Now;
                if (room.Subscriptions.Count == 0 && Now - retainFrom > TimeSpan.FromMinutes(Settings.RetentionMinutes))
                {
                    await store.DeleteAsync(room.MatchId, ct);
                    lock (_registry) _rooms.TryRemove(new KeyValuePair<int, Entry>(room.MatchId, entry));
                    return;
                }
                if (room.IsMock && room.Initialized && room.Subscriptions.Count > 0 && !room.ClosedAt.HasValue &&
                    Settings.MockAutoAdvanceSeconds > 0 && Now >= room.MockNextAdvance)
                {
                    room.MockStage++;
                    Merge(room, MatchLiveMock.Build(room.MockStage, room.Match.MatchInfo.StartTime.AddMinutes(5)), initial: false);
                    room.MockNextAdvance = Now.AddSeconds(Settings.MockAutoAdvanceSeconds);
                    await store.SaveAsync(room, ct);
                }
                if (!room.Initialized || room.IsMock || room.ClosedAt.HasValue || room.Subscriptions.Count == 0 || room.NextPoll > Now) return;
                try
                {
                    var lastGame = room.Match.EventList.LastOrDefault(e => e.Game is not null);
                    var pending = lastGame?.Game?.EndTime is null ? lastGame : null;
                    var cursor = pending?.Id - 1 ?? (room.Match.EventList.LastOrDefault()?.Id ?? 0);
                    // Catch up every page; overlapping in-progress events are merged, never just discarded.
                    for (var page = 0; page < 20; page++)
                    {
                        var next = await QueryAsync(() => api.GetMatchAfterAsync(room.MatchId, cursor, ct), ct);
                        Merge(room, next, initial: false);
                        var last = next.EventList.Count == 0 ? cursor : next.EventList.Max(e => e.Id);
                        if (last >= next.LatestEventId || next.EventList.Count < 100) break;
                        if (last <= cursor) throw new InvalidOperationException("直播事件分页游标未前进。");
                        cursor = last;
                        if (page == 19) throw new InvalidOperationException("直播事件积压超过单次追踪上限。");
                    }
                    if (room.Error is not null) Add(room, "recovered", null, "比赛查询已恢复。");
                    room.Error = null; room.Failures = 0;
                    room.LastSuccess = Now;
                    room.NextPoll = Now.AddSeconds(Settings.PollSeconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (room.Error is null) Add(room, "polling-error", null, "查询暂时失败，正在重试。");
                    room.Error = "查询暂时失败，正在重试。";
                    room.Failures++;
                    room.NextPoll = Now.AddSeconds(Math.Min(120, Settings.PollSeconds * Math.Pow(2, Math.Min(room.Failures, 4))));
                    logger.LogWarning(ex, "Match live poll failed for {MatchId}", room.MatchId);
                }
                await store.SaveAsync(room, ct);
            }
            finally { entry.Gate.Release(); }
        });
    }

    public async Task<LiveSnapshot> AdvanceMockAsync(CancellationToken ct = default)
    {
        if (!Settings.MockEnabled) throw new KeyNotFoundException("开发 mock 未启用。");
        await RestoreAsync(ct);
        if (!_rooms.TryGetValue(MatchLiveMock.MatchId, out var entry)) throw new KeyNotFoundException("请先订阅 mock 比赛。");
        await entry.Gate.WaitAsync(ct);
        try
        {
            if (entry.Room.MockStage < 3)
            {
                entry.Room.MockStage++;
                entry.Room.MockNextAdvance = Now.AddSeconds(Settings.MockAutoAdvanceSeconds);
                Merge(entry.Room, MatchLiveMock.Build(entry.Room.MockStage, entry.Room.Match.MatchInfo.StartTime.AddMinutes(5)), initial: false);
                await store.SaveAsync(entry.Room, ct);
            }
            return Snapshot(entry.Room);
        }
        finally { entry.Gate.Release(); }
    }

    public async Task<LiveSnapshot> ResetMockAsync(CancellationToken ct = default)
    {
        if (!Settings.MockEnabled) throw new KeyNotFoundException("开发 mock 未启用。");
        await RestoreAsync(ct);
        if (!_rooms.TryGetValue(MatchLiveMock.MatchId, out var entry)) throw new KeyNotFoundException("请先订阅 mock 比赛。");
        await entry.Gate.WaitAsync(ct);
        try
        {
            var room = entry.Room;
            room.MockStage = 0; room.ClosedAt = null; room.Error = null; room.Failures = 0;
            room.Match = MatchLiveMock.Build(0, Now);
            room.LastSuccess = Now;
            room.MockNextAdvance = Now.AddSeconds(Settings.MockAutoAdvanceSeconds);
            Add(room, "mock-reset", null, "模拟重放已重新开始。");
            await store.SaveAsync(room, ct);
            return Snapshot(room);
        }
        finally { entry.Gate.Release(); }
    }

    private async Task<MatchResponse> LoadInitialAsync(int matchId, CancellationToken ct)
    {
        var initial = await QueryAsync(() => api.GetMatchAsync(matchId, cancellationToken: ct), ct);
        var events = initial.EventList.ToDictionary(e => e.Id);
        var users = initial.Users.ToDictionary(u => u.Id);
        var pages = 0;
        while (events.Count > 0 && events.Keys.Min() > initial.FirstEventId)
        {
            if (++pages > 20) throw new InvalidOperationException("初始比赛历史超过单次加载上限。");
            var before = events.Keys.Min();
            var previous = await QueryAsync(() => api.GetMatchAsync(matchId, before, ct), ct);
            if (previous.MatchInfo.Id != matchId || previous.EventList.Count == 0 || previous.EventList.Min(e => e.Id) >= before)
                throw new InvalidOperationException("初始比赛历史分页不完整。");
            foreach (var item in previous.EventList) events.TryAdd(item.Id, item);
            foreach (var user in previous.Users) users.TryAdd(user.Id, user);
        }
        initial.EventList = events.Values.OrderBy(e => e.Id).ToList();
        initial.Users = users.Values.ToList();
        return initial;
    }

    private async Task<MatchResponse> QueryAsync(Func<Task<MatchResponse>> query, CancellationToken ct)
    {
        using var permit = await _budget.AcquireAsync(1, ct);
        if (!permit.IsAcquired) throw new LiveBudgetExceededException();
        return await query();
    }

    private void Merge(LiveRoom room, MatchResponse next, bool initial)
    {
        if (next.MatchInfo.Id != room.MatchId) throw new InvalidOperationException("上游返回了错误的比赛 ID。");
        var events = room.Match.EventList.ToDictionary(e => e.Id);
        foreach (var item in next.EventList.OrderBy(e => e.Id))
        {
            events.TryGetValue(item.Id, out var previous);
            if (previous?.Game?.EndTime is not null && item.Game?.EndTime is null) continue;
            var changed = previous is null || JsonConvert.SerializeObject(previous, OsuClient.BuildJsonSettings()) !=
                JsonConvert.SerializeObject(item, OsuClient.BuildJsonSettings());
            events[item.Id] = item;
            if (!initial && changed)
            {
                var type = item.Game is { } game ? game.EndTime.HasValue ? game.Scores.Count == 0 ? "game-aborted" : "game-finished" : "game-started"
                    : item.Detail.Type switch
                    {
                        MatchEventType.PlayerJoined => "player-joined", MatchEventType.PlayerLeft => "player-left",
                        MatchEventType.PlayerKicked => "player-kicked", MatchEventType.HostChanged => "host-changed",
                        MatchEventType.MatchCreated => "room-created", MatchEventType.MatchDisbanded => "room-closed", _ => "room-event"
                    };
                var name = next.Users.Concat(room.Match.Users).FirstOrDefault(u => u.Id == item.UserId)?.Username ?? item.UserId?.ToString() ?? "";
                Add(room, type, item, type switch
                {
                    "player-joined" => $"{name} 加入房间", "player-left" => $"{name} 离开房间",
                    "player-kicked" => $"{name} 被移出房间", "host-changed" => $"{name} 成为房主",
                    "game-started" => "对局开始", "game-finished" => "对局结束", "game-aborted" => "对局中止",
                    "room-closed" => "房间关闭", "room-created" => "房间创建", _ => item.Detail.Text ?? "房间事件"
                });
            }
        }
        next.EventList = events.Values.OrderBy(e => e.Id).ToList();
        next.Users = room.Match.Users.Concat(next.Users).GroupBy(u => u.Id).Select(g => g.Last()).ToList();
        // A game may still be unresolved when the room closes. Keep it; do not invent final scores.
        room.Match = next;
        room.LastSuccess = Now;
        room.NextPoll = Now.AddSeconds(Settings.PollSeconds);
        if (next.MatchInfo.EndTime.HasValue || next.EventList.Any(e => e.Detail.Type == MatchEventType.MatchDisbanded))
        {
            if (!room.ClosedAt.HasValue && !initial && !room.Updates.Any(u => u.Type == "room-closed"))
                Add(room, "room-closed", null, "房间关闭");
            room.ClosedAt ??= Now;
        }
    }

    private void Add(LiveRoom room, string type, MatchEvent? item, string text)
    {
        room.Updates.Add(new LiveUpdate(++room.Revision, type, item?.Id, item?.Game?.Id, text, item?.Timestamp ?? Now, Clone(item)));
        if (room.Updates.Count > Settings.MaxUpdates) room.Updates.RemoveRange(0, room.Updates.Count - Settings.MaxUpdates);
    }

    public void Dispose() => _budget.Dispose();

    private static LiveSnapshot Snapshot(LiveRoom room)
    {
        var lastGame = room.Match.EventList.LastOrDefault(e => e.Game is not null)?.Game;
        var game = lastGame?.EndTime is null ? lastGame : null;
        var status = room.ClosedAt.HasValue ? "closed" : game is not null ? "playing" : "waiting";
        return new(room.MatchId, room.Match.MatchInfo.Name, status, room.IsMock, room.Revision,
            room.LastSuccess, room.Error, Clone(game), room.Match.EventList.Where(e => e.Game?.EndTime.HasValue == true).Select(e => e.Game!.Id).Distinct().Count(), Clone(room.Match.Users));
    }
}

public sealed class LiveBudgetExceededException() : RetryableException("实时比赛查询预算已耗尽，请稍后重试。");

public sealed class MatchLiveWorker(MatchLiveService live, ILogger<MatchLiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try { await live.TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Match live scheduler failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
