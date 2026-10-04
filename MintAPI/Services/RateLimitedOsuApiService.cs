using System.Threading.RateLimiting;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Services;

/// <summary>osu! API 配额与等待队列均已满。</summary>
public sealed class OsuQuotaExceededException() : RetryableException("osu! API 配额已耗尽");

/// <summary>统一限制 osu! API 调用速率。</summary>
public sealed class RateLimitedOsuApiService(
    IOsuApiService inner,
    RateLimiter limiter) : IOsuApiService
{
    public Task<User> GetUserAsync(string userId, GameMode? mode = null)
        => ExecuteAsync(() => inner.GetUserAsync(userId, mode));

    public Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0, bool? includeFails = null, bool? legacyOnly = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => inner.GetUserScoresAsync(userId, type, mode, limit, offset, includeFails, legacyOnly, cancellationToken), cancellationToken);

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
