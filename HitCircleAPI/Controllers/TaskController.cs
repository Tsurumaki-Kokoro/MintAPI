using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HitCircleAPI.Controllers;

[ApiController]
[Route("task")]
public class TaskController(
    AppDbContext db,
    IOsuApiService osuApi,
    ILogger<TaskController> logger) : ControllerBase
{
    [HttpPost("update_user_info")]
    public async Task<IActionResult> UpdateUserInfo()
    {
        var users = await db.Users.ToListAsync();
        var errors = new List<string>();

        foreach (var user in users)
        {
            for (int gameModeInt = 0; gameModeInt < 4; gameModeInt++)
            {
                try
                {
                    var mode = (Ossapi.Enums.GameMode)gameModeInt;
                    var osuUser = await osuApi.GetUserAsync(user.OsuUid, mode);
                    var stats = osuUser.Statistics;

                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var existing = await db.UserOsuInfoHistories
                        .FirstOrDefaultAsync(h => h.OsuUid == user.OsuUid && h.GameMode == gameModeInt && h.Date == today);

                    if (existing is not null)
                    {
                        existing.CountryRank = stats?.CountryRank;
                        existing.GlobalRank = stats?.GlobalRank;
                        existing.Pp = stats?.Pp;
                        existing.Accuracy = stats?.HitAccuracy;
                        existing.PlayCount = stats?.PlayCount;
                        existing.PlayTime = stats?.PlayTime;
                        existing.TotalHits = (int?)stats?.TotalHits;
                    }
                    else
                    {
                        db.UserOsuInfoHistories.Add(new UserOsuInfoHistory
                        {
                            OsuUid = user.OsuUid,
                            GameMode = gameModeInt,
                            CountryRank = stats?.CountryRank,
                            GlobalRank = stats?.GlobalRank,
                            Pp = stats?.Pp,
                            Accuracy = stats?.HitAccuracy,
                            PlayCount = stats?.PlayCount,
                            PlayTime = stats?.PlayTime,
                            TotalHits = (int?)stats?.TotalHits,
                            Date = today
                        });
                    }

                    await db.SaveChangesAsync();
                    logger.LogInformation("Updated user info for {OsuUid} mode {Mode}", user.OsuUid, gameModeInt);
                }
                catch (Exception ex) when (ex is not RetryableException)
                {
                    logger.LogError(ex, "Failed to update user info for {OsuUid} mode {Mode}", user.OsuUid, gameModeInt);
                    errors.Add($"{user.OsuUid} mode {gameModeInt}: {ex.Message}");
                }
            }
        }

        if (errors.Count > 0)
            return StatusCode(500, new { message = "Some updates failed", errors });

        return Ok(new { message = "User info updated" });
    }

    [HttpPost("clear_cache")]
    public IActionResult ClearCache()
    {
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
        try
        {
            var beatmapCacheDir = Path.Combine(cacheDir, "beatmap", "osu_file");
            if (Directory.Exists(beatmapCacheDir))
            {
                foreach (var dir in Directory.GetDirectories(beatmapCacheDir))
                    Directory.Delete(dir, recursive: true);
            }

            var userCacheDir = Path.Combine(cacheDir, "user");
            if (Directory.Exists(userCacheDir))
            {
                foreach (var userDir in Directory.GetDirectories(userCacheDir))
                {
                    var dirName = Path.GetFileName(userDir);
                    foreach (var file in Directory.GetFiles(userDir))
                    {
                        if (Path.GetFileNameWithoutExtension(file) == dirName)
                            System.IO.File.Delete(file);
                    }
                    if (!Directory.EnumerateFileSystemEntries(userDir).Any())
                        Directory.Delete(userDir);
                }
            }
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to clear cache");
            return StatusCode(500, ex.Message);
        }

        return Ok(new { message = "Cache cleared" });
    }

    [HttpPost("pack_logs")]
    public IActionResult PackLogs()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDir))
            return NotFound("No logs directory found");

        try
        {
            var zipPath = Path.Combine(Path.GetTempPath(), "logs.zip");
            if (System.IO.File.Exists(zipPath))
                System.IO.File.Delete(zipPath);

            System.IO.Compression.ZipFile.CreateFromDirectory(logDir, zipPath);
            var bytes = System.IO.File.ReadAllBytes(zipPath);
            System.IO.File.Delete(zipPath);
            return File(bytes, "application/zip", "logs.zip");
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to pack logs");
            return StatusCode(500, ex.Message);
        }
    }
}
