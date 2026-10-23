using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Models.Entities;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Services.PlayerFeatures;

public sealed class PlayerReportService(AppDbContext db, IOsuApiService api, HistoryService history,
    IOptions<HistoryOptions> options)
{
    internal static readonly JsonSerializerSettings JsonSettings = new() { DateParseHandling = DateParseHandling.None };
    public static List<Score> Restore(string json) => JsonConvert.DeserializeObject<List<Score>>(json, JsonSettings)
        ?? throw new InvalidOperationException("Invalid BP snapshot");

    public async Task<User> UserAsync(string uid, int mode, CancellationToken ct)
    {
        try { return await api.GetUserAsync(uid, (GameMode)mode).WaitAsync(ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        { throw new ApiLookupException(ErrorCatalog.OsuUserNotFound, ex); }
    }

    public async Task CaptureBestAsync(int uid, int mode, DateOnly date, CancellationToken ct)
    {
        if (await db.BestPlaySnapshots.AnyAsync(x => x.UserId == uid && x.GameMode == mode && x.Date == date, ct)) return;
        var scores = await api.GetUserScoresAsync(uid, ScoreType.Best, (GameMode)mode, limit: 100,
            legacyOnly: false, cancellationToken: ct);
        db.BestPlaySnapshots.Add(new BestPlaySnapshot
        {
            UserId = uid, GameMode = mode, Date = date, ObservedAt = DateTimeOffset.UtcNow,
            Payload = JsonConvert.SerializeObject(scores)
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<WeeklyReport> WeeklyAsync(string uid, int mode, DateOnly start, CancellationToken ct)
    {
        var user = await UserAsync(uid, mode, ct);
        var end = start.AddDays(7);
        var rows = await db.UserOsuInfoHistories.AsNoTracking()
            .Where(x => x.OsuUid == user.Id.ToString() && x.GameMode == mode && x.Date >= start && x.Date <= end)
            .OrderBy(x => x.Date).ToListAsync(ct);
        var bp = await db.BestPlaySnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.GameMode == mode && x.Date == end, ct);
        if (rows.Count == 0 && bp == null) throw new ApiLookupException(ErrorCatalog.HistoryNotFound, new InvalidOperationException("No weekly snapshots"));
        var zone = options.Value.Zone;
        var scores = bp == null ? [] : Restore(bp.Payload);
        // Only use a complete end-of-period list, never today's live BP list for an old report.
        var best = scores.Select((s, i) => new RankedBest(i + 1, s)).Where(x =>
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Score.EndedAt, zone).DateTime);
            return date >= start && date < end;
        }).ToList();
        return new(user, mode, start, end, rows.SingleOrDefault(x => x.Date == start),
            rows.SingleOrDefault(x => x.Date == end), rows, best, bp != null, bp?.ObservedAt, options.Value.TimeZone);
    }

    public async Task<ScoreComparison> CompareAsync(string left, string right, int mapId, int mode,
        string? mods, bool? legacyOnly, CancellationToken ct)
    {
        var a = await UserAsync(left, mode, ct);
        var b = await UserAsync(right, mode, ct);
        var first = await history.GetMapScoresAsync(a.Id, mapId, mode, ct);
        var second = await history.GetMapScoresAsync(b.Id, mapId, mode, ct);
        var x = Select(first, mods, legacyOnly);
        var y = Select(second, mods, legacyOnly);
        if (x == null || y == null)
            throw new ApiLookupException((x == null && first.Source == "local" && first.Scores.Count == 0)
                || (y == null && second.Source == "local" && second.Scores.Count == 0)
                ? ErrorCatalog.LocalScoreNotCollected : ErrorCatalog.ScoreNotFound,
                new InvalidOperationException("One comparison side has no matching score"));
        var map = x.Beatmap ?? y.Beatmap ?? await api.GetBeatmapAsync(mapId).WaitAsync(ct);
        return new(map, mode, new(a, x, first.Source), new(b, y, second.Source),
            ModKey(x, false) == ModKey(y, false) && IsLegacy(x) == IsLegacy(y)
            && (IsLegacy(x) || x.BuildId.HasValue && x.BuildId == y.BuildId));
    }

    public static bool IsLegacy(Score s) => s.LegacyScoreId is > 0;
    // Include settings (e.g. custom clock rate) when deciding whether scores are comparable.
    public static string ModKey(Score s, bool acronymsOnly) => string.Join("|", (s.Mods ?? [])
        .Where(m => m.Acronym != "CL").Select(m => acronymsOnly ? m.Acronym.ToUpperInvariant()
            : m.Acronym.ToUpperInvariant() + ":" + JsonConvert.SerializeObject(m.Settings?.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value)))
        .OrderBy(x => x, StringComparer.Ordinal));
    private static Score? Select(MapScoreHistory result, string? mods, bool? legacyOnly)
    {
        var list = result.Scores.AsEnumerable();
        if (legacyOnly.HasValue) list = list.Where(s => IsLegacy(s) == legacyOnly.Value);
        if (mods != null)
        {
            var key = string.Join("|", mods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.ToUpperInvariant()).Where(x => x is not ("NM" or "CL")).Distinct().OrderBy(x => x, StringComparer.Ordinal));
            list = list.Where(x => ModKey(x, true) == key);
        }
        // Consistent selection across both official best-per-mod and locally collected attempts.
        return list.OrderByDescending(x => x.Pp.HasValue).ThenByDescending(x => x.Pp)
            .ThenByDescending(x => x.TotalScore).ThenByDescending(x => x.EndedAt).FirstOrDefault();
    }
}
