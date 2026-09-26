using Ossapi.Models;
using Ossapi.Enums;

namespace HitCircleAPI.Services;

public interface IOsuApiService
{
    Task<User> GetUserAsync(string userId, GameMode? mode = null);
    Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0);
    Task<Beatmap> GetBeatmapAsync(int beatmapId);
    Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId);
    Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null);
    Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync();
}
