using System.Threading.RateLimiting;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Services;

/// <summary>osu! API 配额已耗尽（含排队额度也满了）。</summary>
public sealed class OsuQuotaExceededException() : RetryableException("osu! API 配额已耗尽");

/// <summary>
/// 给所有 osu! API 调用加一道配额闸门。闸门是全局的，不按调用方区分 ——
/// 被争抢的资源是这一个 osu! client 的配额，跟谁在问无关。
/// <para>
/// 配额不足时排队等待（限流器自带队列）；只有连队列都满了才失败。
/// 这跟渲染那边的"立刻失败"相反，因为渲染满了 bot 能自己重试，而 osu! 配额
/// 是 bot 不该知道的内部资源。
/// </para>
/// </summary>
public sealed class RateLimitedOsuApiService(
    IOsuApiService inner,
    RateLimiter limiter) : IOsuApiService
{
    public Task<User> GetUserAsync(string userId, GameMode? mode = null)
        => ExecuteAsync(() => inner.GetUserAsync(userId, mode));

    public Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0)
        => ExecuteAsync(() => inner.GetUserScoresAsync(userId, type, mode, limit, offset));

    public Task<Beatmap> GetBeatmapAsync(int beatmapId)
        => ExecuteAsync(() => inner.GetBeatmapAsync(beatmapId));

    public Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId)
        => ExecuteAsync(() => inner.GetBeatmapsetAsync(beatmapsetId));

    public Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null)
        => ExecuteAsync(() => inner.GetBeatmapUserScoresAsync(beatmapId, userId, mode));

    public Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync()
        => ExecuteAsync(inner.GetSeasonalBackgroundsAsync);

    public Task<MatchResponse> GetMatchAsync(int matchId, long? beforeId = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => inner.GetMatchAsync(matchId, beforeId, cancellationToken), cancellationToken);

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken = default)
    {
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new OsuQuotaExceededException();

        return await call();
    }
}
