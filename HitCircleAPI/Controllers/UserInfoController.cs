using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Rendering.UserInfoTheme;
using HitCircleAPI.Rendering.PerformanceAnalyzeTheme;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ossapi.Enums;

namespace HitCircleAPI.Controllers;

/// <summary>用户资料卡。</summary>
[ApiController]
[Route("user_info")]
public class UserInfoController(
    AppDbContext db,
    Microsoft.Extensions.Options.IOptions<HistoryOptions> historyOptions,
    IOsuApiService osuApi,
    IImageCacheService imageCache,
    DefaultUserInfoTheme userInfoTheme,
    PerformanceAnalyzeTheme performanceAnalyzeTheme,
    IPpCalculatorService ppCalc,
    ILogger<UserInfoController> logger) : ControllerBase
{
    /// <summary>渲染用户资料卡。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="game_mode">游戏模式（0=osu!，1=taiko，2=catch，3=mania），默认沿用绑定时的模式。</param>
    /// <param name="user_name">改用这个 osu! 用户名取数据，而不是绑定时的账号。</param>
    /// <param name="compare_with">与 N 天前的数据对比；取历史表里最接近那天的一条。</param>
    /// <param name="theme">渲染主题：default 或 apple，默认 default。</param>
    /// <response code="200">渲染好的 PNG 资料卡。</response>
    /// <response code="400">不支持的渲染主题。</response>
    /// <response code="404">用户未绑定。</response>
    /// <response code="500">取用户信息或渲染失败。</response>
    [HttpGet]
    [Produces("image/png")]
    public async Task<IActionResult> GetUserInfo(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] int? game_mode = null,
        [FromQuery] string? user_name = null,
        [FromQuery] int? compare_with = null,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "apple"))
            return BadRequest("Unsupported user info theme. Use default or apple.");

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
            var compareDate = historyOptions.Value.Today(DateTimeOffset.UtcNow).AddDays(-compare_with.Value);
            // Try to find the record closest to the target date
            var candidates = await db.UserOsuInfoHistories
                .Where(h => h.OsuUid == userInfo.Id.ToString() && h.GameMode == gameModeInt)
                .ToListAsync();
            history = candidates
                .OrderBy(h => Math.Abs(h.Date.DayNumber - compareDate.DayNumber))
                .FirstOrDefault();
        }

        byte[] image;
        try
        {
            image = await userInfoTheme.RenderAsync(userInfo, history, modeStr.ToUpper(), theme);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render user info image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }

    /// <summary>上传资料卡背景图。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="background_file">背景图文件。</param>
    /// <response code="200">上传成功。</response>
    /// <response code="400">文件为空。</response>
    /// <response code="404">用户未绑定。</response>
    /// <response code="500">写缓存失败。</response>
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

    /// <summary>给定 pp 值，算出需要多少 pp 才能挤进 BP 列表、以及会排在第几位。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="pp">目标 pp 值。</param>
    /// <response code="200">JSON：required_pp 与 position。</response>
    /// <response code="400">取成绩失败。</response>
    /// <response code="404">用户未绑定，或没有成绩记录。</response>
    /// <response code="500">取用户信息失败。</response>
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

    /// <summary>渲染 BP 成绩分析图。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="theme">渲染主题：default 或 apple，均使用苹果风格，默认 default。</param>
    /// <response code="200">包含 BP 曲线、评级、星数、Mod、Mapper 等分析数据的 PNG 图片。</response>
    /// <response code="400">主题不支持或取成绩失败。</response>
    /// <response code="404">用户未绑定，或没有成绩记录。</response>
    /// <response code="500">取用户信息或渲染失败。</response>
    [HttpGet("extra/performance_analyze")]
    [Produces("image/png")]
    public async Task<IActionResult> PerformanceAnalyze(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "apple"))
            return BadRequest("Unsupported performance analysis theme. Use default or apple.");

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
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Best, mode, limit: 100);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No play record found");

        try
        {
            var image = await performanceAnalyzeTheme.RenderAsync(userInfo, scores,
                GameModeToString(userModel.GameMode).ToUpperInvariant());
            return File(image, "image/png");
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render BP analysis image for user {UserId}", userInfo.Id);
            return StatusCode(500, "Failed to render BP analysis image");
        }
    }

    private static string GameModeToString(int mode) => mode switch
    {
        1 => "taiko",
        2 => "fruits",
        3 => "mania",
        _ => "osu"
    };
}
