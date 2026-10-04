using MintOsuApi;
using MintOsuApi.Models;
using MintOsuApi.Enums;

namespace MintAPI.Services;

public class OsuApiService : IOsuApiService, IDisposable
{
    private readonly OsuClient _client;

    public OsuApiService(IConfiguration config)
    {
        var clientId = config.GetValue<int>("OsuApi:ClientId");
        var clientSecret = config["OsuApi:ClientSecret"]!;
        var tokenDir = Path.Combine(AppContext.BaseDirectory, "token_cache");
        _client = new OsuClient(clientId, clientSecret, tokenDir);
    }

    public Task<User> GetUserAsync(string userId, GameMode? mode = null)
        => _client.GetUserAsync(userId, mode);

    public Task<List<Score>> GetUserScoresAsync(int userId, ScoreType type, GameMode? mode = null, int limit = 100, int offset = 0, bool? includeFails = null, bool? legacyOnly = null, CancellationToken cancellationToken = default)
        => _client.GetUserScoresAsync(userId, type, mode: mode, limit: limit, offset: offset, includeFails: includeFails, legacyOnly: legacyOnly, ct: cancellationToken);

    public Task<Beatmap> GetBeatmapAsync(int beatmapId)
        => _client.GetBeatmapAsync(beatmapId);

    public Task<Beatmapset> GetBeatmapsetAsync(int beatmapsetId)
        => _client.GetBeatmapsetAsync(beatmapsetId);

    public Task<List<Score>> GetBeatmapUserScoresAsync(int beatmapId, int userId, GameMode? mode = null)
        => _client.GetBeatmapUserScoresAsync(beatmapId, userId, mode: mode);

    public Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync()
        => _client.GetSeasonalBackgroundsAsync();

    public Task<MatchResponse> GetMatchAsync(int matchId, long? beforeId = null, CancellationToken cancellationToken = default)
        => _client.GetMatchAsync(matchId, beforeId: beforeId, limit: 100, ct: cancellationToken);

    public void Dispose() => _client.Dispose();
}
