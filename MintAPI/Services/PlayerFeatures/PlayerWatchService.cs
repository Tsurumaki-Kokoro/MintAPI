using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Models.Entities;
using MintAPI.Rendering.PlayerFeatures;
using MintOsuApi.Enums;
using Newtonsoft.Json;

namespace MintAPI.Services.PlayerFeatures;

public sealed class PlayerWatchConflictException() : RetryableException("玩家订阅状态正在更新，请重试");

public sealed class PlayerWatchService(AppDbContext db, IOsuApiService api, PlayerReportService reports,
    IOptions<PlayerWatchOptions> options, IOptions<HistoryOptions> historyOptions)
{
    public async Task<SubscriptionResult> SubscribeAsync(string uid, int mode, string scope, CancellationToken ct)
    {
        var user = await reports.UserAsync(uid, mode, ct);
        var now = DateTimeOffset.UtcNow;
        var group = PlayerWatchGroup.Parse(scope);
        if (group != null) scope = group.Scope;
        var candidates = await db.PlayerSubscriptions.Where(x => x.UserId == user.Id && x.GameMode == mode).ToListAsync(ct);
        var existing = candidates.Where(x => group != null ? PlayerWatchGroup.Parse(x.Scope) == group : x.Scope == scope)
            .OrderByDescending(x => x.ExpiresAt > now).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (existing != null && existing.ExpiresAt > now)
        {
            existing.ExpiresAt = now.AddDays(options.Value.SubscriptionDays);
            await SaveAsync(ct);
            return new(existing.Id, user.Id, mode, existing.StartCursor, existing.ExpiresAt);
        }
        var subscriptions = await db.PlayerSubscriptions.AsNoTracking().ToListAsync(ct);
        var active = subscriptions.Where(x => x.ExpiresAt > now).ToArray();
        if (active.Length >= options.Value.MaxTrackedUsers * 10 ||
            (!active.Any(x => x.UserId == user.Id && x.GameMode == mode)
            && active.Select(x => (x.UserId, x.GameMode)).Distinct().Count() >= options.Value.MaxTrackedUsers))
            throw new ApiLookupException(ErrorCatalog.Conflict, new InvalidOperationException("Player subscription limit"));
        var watch = await db.PlayerWatches.SingleOrDefaultAsync(x => x.UserId == user.Id && x.GameMode == mode, ct);
        // An inactive watch must establish a fresh baseline: never replay activity from before subscription.
        if (watch == null || !active.Any(x => x.UserId == user.Id && x.GameMode == mode))
        {
            var scores = await api.GetUserScoresAsync(user.Id, ScoreType.Best, (GameMode)mode,
                limit: 100, legacyOnly: false, cancellationToken: ct);
            if (watch == null) { watch = new() { UserId = user.Id, GameMode = mode }; db.PlayerWatches.Add(watch); }
            watch.Revision++;
            watch.Payload = JsonConvert.SerializeObject(new WatchSnapshot(user, scores));
            watch.CheckedAt = DateTimeOffset.UtcNow;
        }
        if (existing != null) db.PlayerSubscriptions.Remove(existing);
        var subscription = new PlayerSubscription
        {
            UserId = user.Id, GameMode = mode, Scope = scope, StartCursor = watch.Revision,
            ExpiresAt = now.AddDays(options.Value.SubscriptionDays)
        };
        db.PlayerSubscriptions.Add(subscription);
        await SaveAsync(ct);
        return new(subscription.Id, user.Id, mode, subscription.StartCursor, subscription.ExpiresAt);
    }

    public async Task<WatchUpdates> UpdatesAsync(string id, long after, CancellationToken ct)
    {
        var sub = await SubscriptionAsync(id, ct);
        var watch = await db.PlayerWatches.AsNoTracking().SingleAsync(x => x.UserId == sub.UserId && x.GameMode == sub.GameMode, ct);
        if (after < sub.StartCursor || after < watch.FloorCursor)
            throw new ApiLookupException(ErrorCatalog.ResourceExpired, new InvalidOperationException("Cursor precedes subscription or retained events"));
        if (after > watch.Revision)
            throw new ApiLookupException(ErrorCatalog.InvalidArgument, new InvalidOperationException("Future cursor"));
        var events = await db.PlayerWatchEvents.AsNoTracking().Where(x => x.UserId == sub.UserId && x.GameMode == sub.GameMode
                && x.Cursor > after && x.Cursor <= watch.Revision).OrderBy(x => x.Cursor).Take(101).ToListAsync(ct);
        var more = events.Count > 100;
        var page = events.Take(100).Select(x => new WatchUpdate(x.Cursor, x.ObservedAt, x.Text)).ToList();
        sub.ExpiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.SubscriptionDays);
        await SaveAsync(ct);
        return new(more ? page[^1].Cursor : watch.Revision, watch.CheckedAt, sub.ExpiresAt, more, page);
    }

    public async Task<PlayerSubscriptionPage> ListAsync(PlayerWatchGroup? group, string? scope, int? userId,
        int? mode, bool includeExpired, int page, int pageSize, CancellationToken ct)
    {
        var rows = await FilterSubscriptionsAsync(group, scope, userId, mode, includeExpired, ct);
        var items = rows.OrderBy(x => x.Scope, StringComparer.Ordinal).ThenBy(x => x.UserId).ThenBy(x => x.GameMode)
            .ThenBy(x => x.Id, StringComparer.Ordinal).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var userIds = items.Select(x => x.UserId).Distinct().ToArray();
        var watches = await db.PlayerWatches.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);
        return new(page, pageSize, rows.Count, items.Select(x => Describe(x,
            watches.FirstOrDefault(w => w.UserId == x.UserId && w.GameMode == x.GameMode))).ToList());
    }

    public async Task<PlayerGroupPage> GroupsAsync(string? platform, bool includeExpired, int page, int pageSize, CancellationToken ct)
    {
        var rows = await FilterSubscriptionsAsync(null, null, null, null, includeExpired, ct);
        var groups = rows.Select(x => (Subscription: x, Group: PlayerWatchGroup.Parse(x.Scope)))
            .Where(x => x.Group != null && (platform == null || x.Group.Platform == platform))
            .GroupBy(x => x.Group!).OrderBy(x => x.Key.Platform, StringComparer.Ordinal)
            .ThenBy(x => x.Key.GroupId, StringComparer.Ordinal).ToList();
        return new(page, pageSize, groups.Count, groups.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlayerGroupInfo(x.Key.Platform, x.Key.GroupId, x.Count(), x.Select(y => y.Subscription.UserId).Distinct().Count())).ToList());
    }

    public async Task<PlayerSubscriptionInfo> GetAsync(string id, PlayerWatchGroup? group, CancellationToken ct)
    {
        var sub = await SubscriptionAsync(id, ct);
        EnsureGroup(sub, group);
        var watch = await db.PlayerWatches.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == sub.UserId && x.GameMode == sub.GameMode, ct);
        return Describe(sub, watch);
    }

    public async Task<int> CancelGroupAsync(PlayerWatchGroup group, int? userId, int? mode, CancellationToken ct)
    {
        // Cancellation is bounded to an explicitly identified group, including its expired subscriptions.
        var rows = await FilterSubscriptionsAsync(group, null, userId, mode, true, ct);
        db.PlayerSubscriptions.RemoveRange(rows);
        await SaveAsync(ct);
        return rows.Count;
    }

    private async Task<List<PlayerSubscription>> FilterSubscriptionsAsync(PlayerWatchGroup? group, string? scope,
        int? userId, int? mode, bool includeExpired, CancellationToken ct)
    {
        var query = db.PlayerSubscriptions.AsQueryable();
        if (userId.HasValue) query = query.Where(x => x.UserId == userId);
        if (mode.HasValue) query = query.Where(x => x.GameMode == mode);
        if (scope != null) query = query.Where(x => x.Scope == scope);
        // Expiry comparison stays in memory for both the MySQL and SQLite providers.
        var rows = await query.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        return rows.Where(x => (includeExpired || x.ExpiresAt > now)
            && (group == null || PlayerWatchGroup.Parse(x.Scope) == group)).ToList();
    }

    private static PlayerSubscriptionInfo Describe(PlayerSubscription sub, PlayerWatch? watch)
    {
        var group = PlayerWatchGroup.Parse(sub.Scope);
        var snapshot = watch == null ? null : JsonConvert.DeserializeObject<WatchSnapshot>(watch.Payload, PlayerReportService.JsonSettings);
        return new(sub.Id, sub.UserId, snapshot?.User.Username, sub.GameMode, sub.Scope, group?.Platform, group?.GroupId,
            sub.StartCursor, sub.ExpiresAt, sub.ExpiresAt <= DateTimeOffset.UtcNow, watch?.CheckedAt);
    }

    private static void EnsureGroup(PlayerSubscription sub, PlayerWatchGroup? group)
    {
        if (group != null && PlayerWatchGroup.Parse(sub.Scope) != group)
            throw new ApiLookupException(ErrorCatalog.RecordNotFound, new InvalidOperationException("Subscription is outside the requested group"));
    }

    public async Task UnsubscribeAsync(string id, CancellationToken ct, PlayerWatchGroup? group = null)
    {
        var sub = await SubscriptionAsync(id, ct);
        EnsureGroup(sub, group);
        db.PlayerSubscriptions.Remove(sub);
        await SaveAsync(ct);
    }

    private async Task<PlayerSubscription> SubscriptionAsync(string id, CancellationToken ct)
    {
        var sub = await db.PlayerSubscriptions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (sub == null || sub.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ApiLookupException(ErrorCatalog.ResourceExpired, new InvalidOperationException("Player subscription expired"));
        return sub;
    }

    public async Task PollAsync(int uid, int mode, CancellationToken ct)
    {
        var watch = await db.PlayerWatches.SingleAsync(x => x.UserId == uid && x.GameMode == mode, ct);
        var now = DateTimeOffset.UtcNow;
        if (watch.CheckedAt > now.AddSeconds(-options.Value.PollSeconds)) return;
        var previous = JsonConvert.DeserializeObject<WatchSnapshot>(watch.Payload, PlayerReportService.JsonSettings)
            ?? throw new InvalidOperationException("Invalid player watch state");
        var user = await reports.UserAsync(uid.ToString(CultureInfo.InvariantCulture), mode, ct);
        var scores = await api.GetUserScoresAsync(uid, ScoreType.Best, (GameMode)mode,
            limit: 100, legacyOnly: false, cancellationToken: ct);
        var current = new WatchSnapshot(user, scores);
        var text = BuildText(previous, current, watch.CheckedAt, mode, historyOptions.Value.Zone);
        watch.Revision++;
        watch.Payload = JsonConvert.SerializeObject(current);
        watch.CheckedAt = now;
        if (text.Length > 0) db.PlayerWatchEvents.Add(new()
        {
            UserId = uid, GameMode = mode, Cursor = watch.Revision, ObservedAt = now, Text = text
        });
        // Retain a bounded seven-to-ninety-day replay window; report stale cursors explicitly.
        var old = (await db.PlayerWatchEvents.Where(x => x.UserId == uid && x.GameMode == mode).ToListAsync(ct))
            .Where(x => x.ObservedAt < now.AddDays(-options.Value.SubscriptionDays)).ToList();
        if (old.Count > 0)
        {
            watch.FloorCursor = Math.Max(watch.FloorCursor, old.Max(x => x.Cursor));
            db.PlayerWatchEvents.RemoveRange(old);
        }
        await SaveAsync(ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new PlayerWatchConflictException(); }
        // Two instances can race to establish the same unique watch/subscription. A retry reads the winner.
        catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        { db.ChangeTracker.Clear(); throw new PlayerWatchConflictException(); }
    }

    public static string BuildText(WatchSnapshot previous, WatchSnapshot current, DateTimeOffset since, int mode, TimeZoneInfo zone)
    {
        var oldIds = previous.Scores.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
        var added = current.Scores.Select((s, i) => new RankedBest(i + 1, s))
            .Where(x => x.Score.Id is > 0 && !oldIds.Contains(x.Score.Id.Value) && x.Score.EndedAt > since).ToList();
        var oldStats = previous.User.Statistics;
        var stats = current.User.Statistics;
        int[] ranks = [100000, 50000, 25000, 10000, 5000, 1000, 500, 100, 50, 10, 1];
        var crossed = oldStats?.GlobalRank is > 0 && stats?.GlobalRank is > 0
            ? ranks.Where(x => oldStats.GlobalRank > x && stats.GlobalRank <= x).ToArray() : [];
        var ppMilestone = oldStats?.Pp is >= 0 && stats?.Pp is > 0
            && Math.Floor(stats.Pp.Value / 1000) > Math.Floor(oldStats.Pp.Value / 1000);
        if (added.Count == 0 && crossed.Length == 0 && !ppMilestone) return "";
        static string T(string? value) => (value ?? "—").Replace('\r', ' ').Replace('\n', ' ');
        static string N(double? x) => x?.ToString("N2", CultureInfo.InvariantCulture) ?? "—";
        var sb = new StringBuilder($"【{(added.Count > 0 ? "新 BP" : "里程碑")}】{T(current.User.Username)} · {PlayerFeatureRenderer.ModeName(mode)}\n");
        foreach (var item in added.Take(10))
        {
            var s = item.Score;
            sb.AppendLine($"{T(s.Beatmapset?.Artist)} - {T(s.Beatmapset?.Title)} [{T(s.Beatmap?.Version)}] {PlayerFeatureRenderer.Mods(s)}");
            sb.AppendLine($"{N(s.Pp)} pp · BP #{item.Position} · {s.Rank} · {s.Accuracy * 100:F2}% · {s.MaxCombo:N0}× · {(s.Statistics?.Miss?.ToString() ?? "—")} miss");
            sb.AppendLine(TimeZoneInfo.ConvertTime(s.EndedAt, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + $"（{zone.Id}）");
        }
        if (added.Count > 10) sb.AppendLine($"另有 {added.Count - 10} 条新 BP");
        sb.AppendLine($"总 PP：{N(oldStats?.Pp)} → {N(stats?.Pp)}" +
            (oldStats?.Pp is double p && stats?.Pp is double q ? $"（{q - p:+0.00;-0.00;0.00}）" : ""));
        static string R(int? x) => x is > 0 ? $"#{x:N0}" : "—";
        sb.AppendLine($"全球排名：{R(oldStats?.GlobalRank)} → {R(stats?.GlobalRank)}" +
            (oldStats?.GlobalRank is > 0 && stats?.GlobalRank is > 0
                ? $"（{(oldStats.GlobalRank >= stats.GlobalRank ? "↑" : "↓")}{Math.Abs(oldStats.GlobalRank.Value - stats.GlobalRank.Value):N0}）" : ""));
        if (crossed.Length > 0) sb.AppendLine($"达成：进入全球前 {crossed.Min():N0}");
        if (ppMilestone) sb.AppendLine($"达成：总 PP 达到 {Math.Floor(stats!.Pp!.Value / 1000) * 1000:N0}");
        sb.Append("注：总 PP 与排名变化为相邻观测差值。");
        return sb.ToString();
    }
}

public sealed class PlayerWatchWorker(IServiceScopeFactory scopes, IOptions<PlayerWatchOptions> options,
    ILogger<PlayerWatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var subscriptions = await db.PlayerSubscriptions.AsNoTracking().ToListAsync(stoppingToken);
                var targets = subscriptions.Where(x => x.ExpiresAt > DateTimeOffset.UtcNow)
                    .Select(x => (x.UserId, x.GameMode)).Distinct().ToArray();
                foreach (var (uid, mode) in targets)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    using var pollScope = scopes.CreateScope();
                    try { await pollScope.ServiceProvider.GetRequiredService<PlayerWatchService>().PollAsync(uid, mode, stoppingToken); }
                    catch (RetryableException ex) { logger.LogWarning(ex, "Player watch busy for {UserId} mode {Mode}; retry later", uid, mode); }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogError(ex, "Player watch failed for {UserId} mode {Mode}; baseline preserved", uid, mode); }
                }
                // Expired scopes are no longer needed, but watches preserve deduplication across restarts.
                db.PlayerSubscriptions.RemoveRange(subscriptions.Where(x => x.ExpiresAt <= DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Player watch scheduler failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
