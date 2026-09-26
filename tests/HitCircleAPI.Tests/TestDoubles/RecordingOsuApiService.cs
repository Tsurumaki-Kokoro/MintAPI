using HitCircleAPI.Services;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Tests.TestDoubles;

/// <summary>记录被调用次数的 <see cref="IOsuApiService"/>，不触碰网络。</summary>
public sealed class RecordingOsuApiService : IOsuApiService
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<User> GetUserAsync(string userId, GameMode? mode = null)
        => Record<User>();

    public Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0)
        => Record<List<Score>>();

    public Task<Beatmap> GetBeatmapAsync(int beatmapId)
        => Record<Beatmap>();

    public Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId)
        => Record<Beatmapset>();

    public Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null)
        => Record<List<Score>>();

    public Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync()
        => Record<SeasonalBackgrounds>();

    private Task<T> Record<T>()
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<T>(default!);
    }
}
