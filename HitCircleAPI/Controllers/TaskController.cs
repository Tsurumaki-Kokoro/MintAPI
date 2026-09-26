using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HitCircleAPI.Controllers;

/// <summary>运维任务：用户数据快照、清缓存、打包日志。</summary>
[ApiController]
[Route("task")]
public class TaskController(
    AppDbContext db,
    IOsuApiService osuApi,
    ILogger<TaskController> logger) : ControllerBase
{
    /// <summary>把所有绑定用户的四种模式数据快照进历史表，供资料卡对比用。</summary>
    /// <remarks>
    /// 每个用户每种模式各发一次 osu! API 请求，串行执行，耗时较长。
    /// 单个失败不会中断整体，失败的条目会收集到响应的 errors 里。
    /// </remarks>
    /// <response code="200">全部成功。</response>
    /// <response code="500">有部分失败，errors 里是失败明细。</response>
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

    /// <summary>清空谱面 osu! 文件缓存与用户头像、背景缓存。</summary>
    /// <response code="200">清理完成。</response>
    /// <response code="500">清理过程中出错。</response>
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

    /// <summary>把日志目录打包成 zip 下载。</summary>
    /// <response code="200">logs.zip。</response>
    /// <response code="404">没有日志目录。</response>
    /// <response code="500">打包失败。</response>
    [HttpPost("pack_logs")]
    [Produces("application/zip")]
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
