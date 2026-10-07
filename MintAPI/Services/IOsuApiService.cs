using MintOsuApi.Models;
using MintOsuApi.Enums;

namespace MintAPI.Services;

public interface IOsuApiService
{
    Task<BeatmapsetSearchResult> SearchBeatmapsetsAsync(string query, BeatmapsetSearchMode mode, BeatmapsetSearchCategory category, BeatmapsetSearchSort? sort, string? cursorString, CancellationToken cancellationToken = default);
    Task<User> GetUserAsync(string userId, GameMode? mode = null);
    Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0, bool? includeFails = null, bool? legacyOnly = null, CancellationToken cancellationToken = default);
    Task<Beatmap> GetBeatmapAsync(int beatmapId);
    Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId);
    Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null);
    Task<MatchResponse> GetMatchAsync(int matchId, long? beforeId = null, CancellationToken cancellationToken = default);
    Task<MatchResponse> GetMatchAfterAsync(int matchId, long afterId, CancellationToken cancellationToken = default);
    Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync();
}
