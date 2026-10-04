using HitCircleAPI.Services;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Tests.TestDoubles;

/// <summary>记录被调用次数的 <see cref="IOsuApiService"/>，不触碰网络。</summary>
public sealed class RecordingOsuApiService : IOsuApiService
{
    private int _callCount;
    public Func<string, GameMode?, User>? UserHandler { get; set; }
    public Func<int, ScoreType, GameMode?, int, int, bool?, bool?, List<Score>>? ScoresHandler { get; set; }

    public Func<int, Beatmap>? BeatmapHandler { get; set; }
    public Func<int, Beatmapset>? BeatmapsetHandler { get; set; }

    public Func<long?, MatchResponse>? MatchHandler { get; set; }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<User> GetUserAsync(string userId, GameMode? mode = null)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(UserHandler?.Invoke(userId, mode) ?? default!);
    }

    public Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0, bool? includeFails = null, bool? legacyOnly = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ScoresHandler?.Invoke(userId, type, mode, limit, offset, includeFails, legacyOnly) ?? default!);
    }

    public Task<Beatmap> GetBeatmapAsync(int beatmapId)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(BeatmapHandler?.Invoke(beatmapId) ?? default!);
    }

    public Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(BeatmapsetHandler?.Invoke(beatmapsetId) ?? default!);
    }

    public Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null)
        => Record<List<Score>>();

    public Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync()
        => Record<SeasonalBackgrounds>();

    public Task<MatchResponse> GetMatchAsync(int matchId, long? beforeId = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MatchHandler?.Invoke(beforeId) ?? new MatchResponse());
    }

    private Task<T> Record<T>()
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<T>(default!);
    }
}
