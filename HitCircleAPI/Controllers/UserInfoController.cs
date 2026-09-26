using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Rendering.UserInfoTheme;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ossapi.Enums;

namespace HitCircleAPI.Controllers;

[ApiController]
[Route("user_info")]
public class UserInfoController(
    AppDbContext db,
    IOsuApiService osuApi,
    IImageCacheService imageCache,
    DefaultUserInfoTheme userInfoTheme,
    IPpCalculatorService ppCalc,
    ILogger<UserInfoController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUserInfo(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] int? game_mode = null,
        [FromQuery] string? user_name = null,
        [FromQuery] int? compare_with = null,
        [FromQuery] string theme = "default")
    {
        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        var gameModeInt = game_mode ?? userModel.GameMode;
        var mode = (GameMode)gameModeInt;
        var modeStr = GameModeToString(gameModeInt);

        Ossapi.Models.User userInfo;
        try
        {
            if (user_name is not null)
                userInfo = await osuApi.GetUserAsync(user_name, mode);
            else
                userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to get user info");
        }

        UserOsuInfoHistory? history = null;
        if (compare_with.HasValue)
        {
            var compareDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-compare_with.Value));
            // Try to find the record closest to the target date
            var candidates = await db.UserOsuInfoHistories
                .Where(h => h.OsuUid == userModel.OsuUid && h.GameMode == gameModeInt)
                .ToListAsync();
            history = candidates
                .OrderBy(h => Math.Abs(h.Date.DayNumber - compareDate.DayNumber))
                .FirstOrDefault();
        }

        byte[] image;
        try
        {
            image = await userInfoTheme.RenderAsync(userInfo, history, modeStr.ToUpper());
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render user info image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }

    [HttpPost("update_background")]
    public async Task<IActionResult> UpdateBackground(
        [FromForm] string platform,
        [FromForm] string platform_uid,
        IFormFile background_file)
    {
        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        if (background_file.Length == 0)
            return BadRequest("No file provided");

        using var ms = new MemoryStream();
        await background_file.CopyToAsync(ms);
        var data = ms.ToArray();

        try
        {
            await imageCache.SaveUserBackgroundAsync(int.Parse(userModel.OsuUid), data);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to save background for user {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to save background");
        }

        return Ok(new { message = "Background updated successfully" });
    }

    [HttpGet("extra/performance_control")]
    public async Task<IActionResult> PerformanceControl(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] double pp)
    {
        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        var mode = (GameMode)userModel.GameMode;

        Ossapi.Models.User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to get user info");
        }

        List<Ossapi.Models.Score> scores;
        try
        {
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, Ossapi.Enums.ScoreType.Best, mode, limit: 100);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No play record found");

        var ppList = scores.Select(s => s.Pp ?? 0.0).ToList();
        var (requiredPp, position) = ppCalc.FindOptimalNewPp(ppList, pp);

        return Ok(new { required_pp = requiredPp, position });
    }

    [HttpGet("extra/performance_analyze")]
    public IActionResult PerformanceAnalyze(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] string theme = "default")
    {
        // BP analyze uses a separate chart-based theme; not yet implemented
        return StatusCode(501, "Performance analyze not yet implemented");
    }

    private static string GameModeToString(int mode) => mode switch
    {
        1 => "taiko",
        2 => "fruits",
        3 => "mania",
        _ => "osu"
    };
}
