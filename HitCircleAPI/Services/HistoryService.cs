using System.Globalization;
using System.Text.Json;
using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Services;

public sealed record HistoryPoint(DateOnly Date, double Pp, int GlobalRank, string Source);
public sealed record MapScoreHistory(string Source, string Notice, List<Score> Scores);

public sealed class HistoryService(
    AppDbContext db, IOsuApiService osuApi, IHttpClientFactory clients,
    IMemoryCache cache, IOptions<HistoryOptions> options, ILogger<HistoryService> logger)
{
    public static Score RestoreScore(string payload) => JsonConvert.DeserializeObject<Score>(payload,
        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;

    public static bool HasLeaderboard(RankStatus status) => status is
        RankStatus.Ranked or RankStatus.Approved or RankStatus.Qualified or RankStatus.Loved;

    public async Task<int> SaveScoresAsync(IEnumerable<Score> scores, CancellationToken ct)
    {
        var eligible = scores.Where(s => s.Id is > 0 && s.UserId > 0 && s.Beatmap != null
                && !HasLeaderboard(s.Beatmap.Status))
            .DistinctBy(s => s.Id).ToArray();
        var ids = eligible.Select(s => s.Id!.Value).ToArray();
        var existing = await db.ScoreHistories.Where(s => ids.Contains(s.ScoreId)).Select(s => s.ScoreId).ToListAsync(ct);
        var missing = eligible.Where(s => !existing.Contains(s.Id!.Value)).ToArray();
        db.ScoreHistories.AddRange(missing.Select(s => new ScoreHistory
        {
            ScoreId = s.Id!.Value, UserId = s.UserId, BeatmapId = s.Beatmap!.Id,
            GameMode = s.RulesetId, EndedAt = s.EndedAt, Payload = JsonConvert.SerializeObject(s)
        }));
        await db.SaveChangesAsync(ct);
        return missing.Length;
    }

    public async Task<MapScoreHistory> GetMapScoresAsync(int userId, int mapId, int mode, CancellationToken ct)
    {
        var map = await osuApi.GetBeatmapAsync(mapId).WaitAsync(ct);
        if (HasLeaderboard(map.Status))
        {
            var official = await osuApi.GetBeatmapUserScoresAsync(mapId, userId, (GameMode)mode).WaitAsync(ct);
            return new("official", "官网当前保留的各 Mod 最佳成绩，不代表全部历史尝试", official);
        }
        var rows = await db.ScoreHistories.AsNoTracking()
            .Where(s => s.UserId == userId && s.BeatmapId == mapId && s.GameMode == mode)
            .OrderByDescending(s => s.EndedAt).ToListAsync(ct);
        var scores = rows.Select(r => RestoreScore(r.Payload)).ToList();
        foreach (var score in scores) { score.Beatmap = map; score.Beatmapset ??= map.Beatmapset; }
        return new("local", "本地采集到的无榜谱面历史，未收录不代表没有游玩过", scores);
    }

    public static List<HistoryPoint> Merge(IEnumerable<HistoryPoint> local, IEnumerable<HistoryPoint> external) =>
        external.Concat(local).Where(p => p.GlobalRank > 0 && double.IsFinite(p.Pp))
            .GroupBy(p => p.Date).Select(g => g.Last()).OrderBy(p => p.Date).ToList();

    public async Task<List<HistoryPoint>> GetUserHistoryAsync(string userId, int mode, int days, CancellationToken ct)
    {
        var today = options.Value.Today(DateTimeOffset.UtcNow);
        var query = db.UserOsuInfoHistories.AsNoTracking().Where(h => h.OsuUid == userId && h.GameMode == mode && h.Date <= today);
        if (days > 0) { var start = today.AddDays(-days); query = query.Where(h => h.Date >= start); }
        var rows = await query.ToListAsync(ct);
        var local = rows.Where(h => h.Pp.HasValue && h.GlobalRank is > 0)
            .Select(h => new HistoryPoint(h.Date, h.Pp!.Value, h.GlobalRank!.Value, "local"));
        List<HistoryPoint> external = [];
        if (options.Value.OsuTrackEnabled)
        {
            var start = today.AddDays(-(days > 0 ? days : options.Value.OsuTrackDefaultDays));
            var key = $"osutrack:{userId}:{mode}:{start}:{today}";
            if (cache.TryGetValue(key, out List<HistoryPoint>? cached)) external = cached!;
            else
            {
                try
                {
                    var client = clients.CreateClient("OsuTrack");
                    using var response = await client.GetAsync($"stats_history?user={Uri.EscapeDataString(userId)}&mode={mode}&from={start:yyyy-MM-dd}&to={today:yyyy-MM-dd}", ct);
                    if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        response.EnsureSuccessStatusCode();
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                        foreach (var item in json.RootElement.EnumerateArray())
                        {
                            if (!item.TryGetProperty("timestamp", out var timestamp) || !item.TryGetProperty("pp_raw", out var pp)
                                || !item.TryGetProperty("pp_rank", out var rank)) continue;
                            var dateText = timestamp.ToString();
                            if (dateText.Length >= 10 && DateOnly.TryParseExact(dateText[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                                && double.TryParse(pp.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                                && int.TryParse(rank.ToString(), out var ranking) && date >= start && date <= today)
                                external.Add(new(date, value, ranking, "osutrack"));
                        }
                    }
                    cache.Set(key, external, TimeSpan.FromMinutes(10));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "osu!track history unavailable for {UserId}; using local history", userId);
                }
            }
        }
        return Merge(local, external);
    }
}
