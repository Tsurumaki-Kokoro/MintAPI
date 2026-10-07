using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Rendering.PerformanceAnalyzeTheme;
using MintAPI.Rendering.UserInfoTheme;
using MintAPI.Services;
using MintOsuApi.Enums;

namespace MintAPI.Controllers;

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
    /// <summary>渲染用户资料卡。default 主题首区使用 osu! 用户 banner，获取失败时回退到上传的背景。</summary>
    /// <param name="platform">绑定平台，如 qq、discord；直接查询用户名时可省略。</param>
    /// <param name="platform_uid">绑定平台用户 ID；指定用户名时用于选择调用者的默认模式，可省略。</param>
    /// <param name="game_mode">模式 0–3，默认使用绑定模式，没有绑定时使用 0。</param>
    /// <param name="user_name">直接指定 osu! 用户名或 UID，无需绑定。</param>
    /// <param name="compare_with">对比 N 天前的最近记录。</param>
    /// <param name="theme">渲染主题：default 或 yaowan。</param>
    /// <response code="200">PNG 资料卡。</response>
    /// <response code="400">主题、模式或查询身份无效。</response>
    /// <response code="404">用户未绑定或指定 osu! 用户不存在。</response>
    /// <response code="500">取用户信息或渲染失败。</response>
    /// <response code="502">上游服务查询失败；返回统一错误 JSON。</response>
    [HttpGet]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> GetUserInfo(
        [FromQuery] string? platform = null,
        [FromQuery] string? platform_uid = null,
        [FromQuery] int? game_mode = null,
        [FromQuery] string? user_name = null,
        [FromQuery] int? compare_with = null,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "yaowan"))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Unsupported user info theme. Use default or yaowan.");
        if (game_mode is < 0 or > 3)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "game_mode must be between 0 and 3.");

        var target = user_name?.Trim();
        var hasBindingIdentity = !string.IsNullOrWhiteSpace(platform) && !string.IsNullOrWhiteSpace(platform_uid);
        if (string.IsNullOrWhiteSpace(target) && !hasBindingIdentity)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Provide user_name or both platform and platform_uid.");
        var userModel = hasBindingIdentity
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid)
            : null;
        if (string.IsNullOrWhiteSpace(target) && userModel is null)
            return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");

        target = string.IsNullOrWhiteSpace(target) ? userModel!.OsuUid : target;
        var gameModeInt = game_mode ?? userModel?.GameMode ?? 0;
        if (gameModeInt is < 0 or > 3)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid bound game mode.");
        var mode = (GameMode)gameModeInt;
        var modeStr = GameModeToString(gameModeInt);

        MintOsuApi.Models.User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(target, mode);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiErrors.Result(ErrorCatalog.OsuUserNotFound, diagnostic: "osu! user not found");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to get user info for {User}", target);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "Failed to get user info");
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
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render user info image");
            return ApiErrors.Result(ErrorCatalog.RenderFailed, diagnostic: $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }

    /// <summary>上传资料卡背景图。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
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
            return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");

        if (background_file.Length == 0)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "No file provided");

        using var ms = new MemoryStream();
        await background_file.CopyToAsync(ms);
        var data = ms.ToArray();

        try
        {
            await imageCache.SaveUserBackgroundAsync(int.Parse(userModel.OsuUid), data);
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to save background for user {OsuUid}", userModel.OsuUid);
            return ApiErrors.Result(ErrorCatalog.InternalError, diagnostic: "Failed to save background");
        }

        return Ok(new { message = "Background updated successfully" });
    }

    /// <summary>计算目标 pp 的 BP 排名和所需 pp。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="pp">目标 pp 值。</param>
    /// <response code="200">JSON：required_pp 与 position。</response>
    /// <response code="400">请求参数无效。</response>
    /// <response code="404">用户未绑定，或没有成绩记录。</response>
    /// <response code="500">内部处理或渲染失败。</response>
    /// <response code="502">上游服务查询失败；返回统一错误 JSON。</response>
    [HttpGet("extra/performance_control")]
    public async Task<IActionResult> PerformanceControl(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] double pp)
    {
        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");

        var mode = (GameMode)userModel.GameMode;

        MintOsuApi.Models.User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiErrors.Result(ErrorCatalog.OsuUserNotFound);
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "Failed to get user info");
        }

        List<MintOsuApi.Models.Score> scores;
        try
        {
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, MintOsuApi.Enums.ScoreType.Best, mode, limit: 100);
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: $"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return ApiErrors.Result(ErrorCatalog.BestPlayNotFound, diagnostic: "No play record found");

        var ppList = scores.Select(s => s.Pp ?? 0.0).ToList();
        var (requiredPp, position) = ppCalc.FindOptimalNewPp(ppList, pp);

        return Ok(new { required_pp = requiredPp, position });
    }

    /// <summary>渲染 BP 成绩分析图。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="game_mode">模式 0–3，省略时使用绑定模式；临时查询不修改绑定。</param>
    /// <param name="theme">渲染主题：default。</param>
    /// <response code="200">PNG 分析图。</response>
    /// <response code="400">主题、模式无效或取成绩失败。</response>
    /// <response code="404">用户未绑定，或没有成绩记录。</response>
    /// <response code="500">取用户信息或渲染失败。</response>
    /// <response code="502">上游服务查询失败；返回统一错误 JSON。</response>
    [HttpGet("extra/performance_analyze")]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> PerformanceAnalyze(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] string theme = "default",
        [FromQuery] int? game_mode = null)
    {
        if (theme != "default")
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Unsupported performance analysis theme. Use default.");
        if (game_mode is < 0 or > 3)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "game_mode must be between 0 and 3.");

        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");

        var gameModeInt = game_mode ?? userModel.GameMode;
        if (gameModeInt is < 0 or > 3)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid bound game mode.");
        var mode = (GameMode)gameModeInt;
        MintOsuApi.Models.User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiErrors.Result(ErrorCatalog.OsuUserNotFound);
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "Failed to get user info");
        }

        List<MintOsuApi.Models.Score> scores;
        try
        {
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Best, mode, limit: 100);
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: $"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return ApiErrors.Result(ErrorCatalog.BestPlayNotFound, diagnostic: "No play record found");

        try
        {
            var image = await performanceAnalyzeTheme.RenderAsync(userInfo, scores,
                GameModeToString(gameModeInt).ToUpperInvariant());
            return File(image, "image/png");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render BP analysis image for user {UserId}", userInfo.Id);
            return ApiErrors.Result(ErrorCatalog.RenderFailed, diagnostic: "Failed to render BP analysis image");
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
