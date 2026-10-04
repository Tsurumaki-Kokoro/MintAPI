using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintOsuApi.Enums;

namespace MintAPI.Services;

public sealed class HistoryScheduler(IServiceScopeFactory scopes, IOptions<HistoryOptions> options,
    ILogger<HistoryScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        var started = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, settings.Zone);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, settings.Zone);
                var date = DateOnly.FromDateTime(now.DateTime);
                foreach (var job in new[] { "info", "scores" })
                {
                    if (job == "scores" && !settings.ScoreCollectionEnabled) continue;
                    var time = HistoryOptions.ParseTime(job == "info" ? settings.InfoTime : settings.ScoreTime);
                    if (TimeOnly.FromDateTime(now.DateTime) < time) continue;
                    if (!settings.CatchUpOnStartup && date == DateOnly.FromDateTime(started.DateTime)
                        && time < TimeOnly.FromDateTime(started.DateTime)) continue;
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await db.Database.OpenConnectionAsync(stoppingToken);
                    var connection = db.Database.GetDbConnection();
                    await using var command = connection.CreateCommand();
                    // Connection-scoped MySQL lock prevents simultaneous collectors across API instances.
                    command.CommandText = "SELECT GET_LOCK('mintapi_history_tasks', 0)";
                    if (Convert.ToInt32(await command.ExecuteScalarAsync(stoppingToken)) != 1) continue;
                    try
                    {
                        if (await db.HistoryTaskRuns.AnyAsync(r => r.Job == job && r.Date == date, stoppingToken)) continue;
                        if (job == "scores" && !await db.HistoryTaskRuns.AnyAsync(r => r.Job == "info" && r.Date == date, stoppingToken)) continue;
                        var failures = await scope.ServiceProvider.GetRequiredService<HistoryCollector>().RunAsync(job, date, stoppingToken);
                        db.HistoryTaskRuns.Add(new() { Job = job, Date = date, FailedTargets = failures, CompletedAt = DateTimeOffset.UtcNow });
                        await db.SaveChangesAsync(stoppingToken);
                        logger.LogInformation("History task {Job} completed for {Date}", job, date);
                    }
                    finally
                    {
                        command.CommandText = "SELECT RELEASE_LOCK('mintapi_history_tasks')";
                        await command.ExecuteScalarAsync(CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "History task failed; retrying at the next scheduler check"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class HistoryCollector(AppDbContext db, IOsuApiService osuApi, HistoryService history,
    IOptions<HistoryOptions> options, ILogger<HistoryCollector> logger)
{
    public static bool IsActive(UserOsuInfoHistory current, UserOsuInfoHistory? previous) =>
        current.PlayCount.HasValue && previous?.PlayCount.HasValue == true && current.PlayCount > previous.PlayCount;

    public async Task<int> RunAsync(string job, DateOnly date, CancellationToken ct)
    {
        var users = await db.Users.Select(u => u.OsuUid).Distinct().ToListAsync(ct);
        var failures = 0;
        var saved = 0;
        foreach (var uid in users)
        {
            for (var mode = 0; mode < 4; mode++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var current = await db.UserOsuInfoHistories.FirstOrDefaultAsync(h => h.OsuUid == uid && h.GameMode == mode && h.Date == date, ct);
                    if (job == "info")
                    {
                        if (current != null) continue;
                        var user = await osuApi.GetUserAsync(uid, (GameMode)mode).WaitAsync(ct);
                        var stats = user.Statistics ?? throw new InvalidOperationException("User statistics missing");
                        db.UserOsuInfoHistories.Add(new()
                        {
                            OsuUid = uid, GameMode = mode, Date = date, CountryRank = stats.CountryRank,
                            GlobalRank = stats.GlobalRank, Pp = stats.Pp, Accuracy = stats.HitAccuracy,
                            PlayCount = stats.PlayCount, PlayTime = stats.PlayTime, TotalHits = checked((int?)stats.TotalHits)
                        });
                        await db.SaveChangesAsync(ct);
                    }
                    else if (current != null)
                    {
                        var previous = await db.UserOsuInfoHistories.AsNoTracking()
                            .Where(h => h.OsuUid == uid && h.GameMode == mode && h.Date < date)
                            .OrderByDescending(h => h.Date).FirstOrDefaultAsync(ct);
                        if (!IsActive(current, previous)) continue;
                        // Sequential requests share the existing global quota gate with interactive API calls.
                        for (var offset = 0; offset < options.Value.RecentLimit; offset += 100)
                        {
                            var limit = Math.Min(100, options.Value.RecentLimit - offset);
                            var scores = await osuApi.GetUserScoresAsync(int.Parse(uid), ScoreType.Recent, (GameMode)mode,
                                limit: limit, offset: offset, includeFails: true, legacyOnly: false, cancellationToken: ct);
                            saved += await history.SaveScoresAsync(scores, ct);
                            if (scores.Count < limit) break;
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failures++;
                    db.ChangeTracker.Clear();
                    logger.LogError(ex, "History {Job} failed for {User} mode {Mode}", job, uid, mode);
                }
            }
        }
        logger.LogInformation("History {Job}: {Users} users, {Saved} scores saved, {Failures} failures", job, users.Count, saved, failures);
        return failures;
    }
}
