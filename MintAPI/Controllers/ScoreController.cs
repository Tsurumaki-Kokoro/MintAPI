using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MintAPI.Data;
using MintAPI.Rendering.ScoreTheme;
using MintAPI.Services;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Controllers;

/// <summary>成绩图：最近游玩、最好成绩、指定谱面成绩。</summary>
[ApiController]
[Route("score")]
public class ScoreController(
    AppDbContext db,
    IOsuApiService osuApi,
    IBeatmapFileService beatmapFile,
    DefaultScoreTheme scoreTheme,
    HistoryService history,
    ILogger<ScoreController> logger) : ControllerBase
{
    /// <summary>渲染最近游玩的单条成绩或区间列表。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="game_mode">模式 0–3，默认使用绑定模式。</param>
    /// <param name="recent_index">最近成绩序号，1–100。</param>
    /// <param name="recent_end">可选最近成绩区间终点（含），最多 20 条；仅 default 支持列表。</param>
    /// <param name="legacy_only">true 仅 Stable 成绩，false 包含 Lazer，省略使用 API 默认。</param>
    /// <param name="include_fails">包含失败成绩。</param>
    /// <param name="theme">渲染主题：default 或 yaowan。</param>
    /// <response code="200">PNG 成绩图。</response>
    /// <response code="400">取成绩失败。</response>
    /// <response code="404">用户未绑定，或没有游玩记录。</response>
    /// <response code="500">取用户信息失败。</response>
    [HttpGet("recent_play")]
    [Produces("image/png")]
    public async Task<IActionResult> RecentPlay(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] int? game_mode = null,
        [FromQuery] bool include_fails = false,
        [FromQuery] string theme = "default",
        [FromQuery] int recent_index = 1,
        [FromQuery] bool? legacy_only = null,
        [FromQuery] int? recent_end = null)
    {
        if (theme is not ("default" or "yaowan")) return BadRequest("theme 必须为 default 或 yaowan。");
        if (game_mode is < 0 or > 3) return BadRequest("game_mode 必须为 0–3。");
        if (recent_index is < 1 or > 100) return BadRequest("recent_index 必须为 1–100。");
        if (recent_end.HasValue && (recent_end < recent_index || recent_end > 100 || recent_end - recent_index >= 20))
            return BadRequest("recent_end 必须不小于 recent_index、不超过 100，每次最多 20 条。");
        if (recent_end.HasValue && theme != "default") return BadRequest("最近游玩列表仅支持 default 主题。");

        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        GameMode? mode = game_mode.HasValue ? (GameMode)game_mode.Value : (GameMode)userModel.GameMode;

        User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to get user info");
        }

        List<Score> scores;
        try
        {
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Recent, mode, limit: (recent_end ?? recent_index) - recent_index + 1, offset: recent_index - 1, includeFails: include_fails, legacyOnly: legacy_only, cancellationToken: HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get recent scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No recent play record found");

        if (recent_end.HasValue)
            return await RenderScoreListAsync(scores, userInfo, recent_index, recent: true);

        return await RenderScoreAsync(scores[0], userInfo, mode, theme, $"RECENT PLAY · #{recent_index}");
    }

    /// <summary>渲染第 N 个最好成绩（BP）或 BP 区间列表。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="game_mode">模式 0–3，默认使用绑定模式。</param>
    /// <param name="legacy_only">true 仅 Stable 成绩，false 包含 Lazer，省略使用 API 默认。</param>
    /// <param name="best_end">可选 BP 区间终点（含），最多 20 条；仅 default 支持列表。</param>
    /// <param name="best_index">第几个 BP，从 1 开始。</param>
    /// <param name="theme">渲染主题：default 或 yaowan。</param>
    /// <response code="200">PNG 成绩图。</response>
    /// <response code="400">取成绩失败。</response>
    /// <response code="404">用户未绑定，或 BP 序号超出成绩数量。</response>
    /// <response code="500">取用户信息失败。</response>
    [HttpGet("best_play")]
    [Produces("image/png")]
    public async Task<IActionResult> BestPlay(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] int? game_mode = null,
        [FromQuery] int best_index = 1,
        [FromQuery] string theme = "default",
        [FromQuery] bool? legacy_only = null,
        [FromQuery] int? best_end = null)
    {
        if (theme is not ("default" or "yaowan")) return BadRequest("theme 必须为 default 或 yaowan。");
        if (game_mode is < 0 or > 3) return BadRequest("game_mode 必须为 0–3。");
        if (best_index is < 1 or > 100) return BadRequest("best_index 必须为 1–100。");

        if (best_end.HasValue && (best_end < best_index || best_end > 100 || best_end - best_index >= 20))
            return BadRequest("best_end 必须不小于 best_index、不超过 100，每次最多 20 条。");
        if (best_end.HasValue && theme != "default") return BadRequest("BP 列表仅支持 default 主题。");

        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        GameMode? mode = game_mode.HasValue ? (GameMode)game_mode.Value : (GameMode)userModel.GameMode;

        User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to get user info");
        }

        List<Score> scores;
        try
        {
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Best, mode, limit: (best_end ?? best_index) - best_index + 1, offset: best_index - 1, legacyOnly: legacy_only, cancellationToken: HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No best play record found");

        if (best_end.HasValue)
            return await RenderScoreListAsync(scores, userInfo, best_index, recent: false);

        return await RenderScoreAsync(scores[0], userInfo, mode, theme, $"BEST PLAY · #{best_index}");
    }

    /// <summary>渲染指定用户在指定谱面上的成绩图。</summary>
    /// <param name="platform">平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="beatmap_id">谱面 ID。</param>
    /// <param name="game_mode">模式 0–3，默认使用绑定模式。</param>
    /// <param name="theme">渲染主题：default 或 yaowan。</param>
    /// <response code="200">PNG 成绩图。</response>
    /// <response code="400">取成绩失败。</response>
    /// <response code="404">用户未绑定，或该谱面没有该用户的成绩。</response>
    /// <response code="500">取用户信息失败。</response>
    [HttpGet("user_score")]
    [Produces("image/png")]
    public async Task<IActionResult> UserScore(
        [FromQuery] string platform,
        [FromQuery] string platform_uid,
        [FromQuery] int beatmap_id,
        [FromQuery] int? game_mode = null,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "yaowan")) return BadRequest("theme 必须为 default 或 yaowan。");
        if (game_mode is < 0 or > 3) return BadRequest("game_mode 必须为 0–3。");

        var userModel = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid);
        if (userModel is null)
            return NotFound("User not found");

        GameMode? mode = game_mode.HasValue ? (GameMode)game_mode.Value : (GameMode)userModel.GameMode;

        User userInfo;
        try
        {
            userInfo = await osuApi.GetUserAsync(userModel.OsuUid, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user info for {OsuUid}", userModel.OsuUid);
            return StatusCode(500, "Failed to get user info");
        }

        List<Score> userScores;
        try
        {
            userScores = (await history.GetMapScoresAsync(userInfo.Id, beatmap_id, (int)mode!.Value, HttpContext.RequestAborted)).Scores;
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user score for beatmap {BeatmapId}", beatmap_id);
            return BadRequest($"Failed to get user score: {ex.Message}");
        }

        if (userScores.Count == 0)
            return NotFound("No score found for this beatmap");

        return await RenderScoreAsync(userScores[0], userInfo, mode, theme, "MAP SCORE");
    }

    private async Task<IActionResult> RenderScoreListAsync(List<Score> scores, User user, int firstIndex, bool recent)
    {
        try
        {
            var image = recent
                ? await scoreTheme.RenderRecentListAsync(scores, user, firstIndex, HttpContext.RequestAborted)
                : await scoreTheme.RenderBestListAsync(scores, user, firstIndex, HttpContext.RequestAborted);
            return File(image, "image/png");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render {ListType} list", recent ? "recent play" : "BP");
            return StatusCode(500, recent ? "Failed to render recent play list" : "Failed to render BP list");
        }
    }

    private async Task<IActionResult> RenderScoreAsync(Score score, User userInfo, GameMode? mode, string theme, string heading)
    {
        var beatmap = score.Beatmap;
        var beatmapset = score.Beatmapset ?? beatmap?.Beatmapset;

        if (beatmap is null)
            return StatusCode(500, "Score has no beatmap info");

        int setId = beatmapset?.Id ?? beatmap.BeatmapsetId;

        string osuFilePath;
        try
        {
            osuFilePath = await beatmapFile.GetOsuFilePathAsync(setId, beatmap.Id);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get osu file for beatmap {BeatmapId}", beatmap.Id);
            return StatusCode(500, "Failed to get beatmap file");
        }

        var bgName = beatmapFile.GetBgFilename(osuFilePath);
        byte[] mapBg;
        try
        {
            mapBg = await beatmapFile.GetMapBgAsync(setId, beatmap.Id, bgName);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get map background for beatmap {BeatmapId}", beatmap.Id);
            return StatusCode(500, "Failed to get map background");
        }

        BeatmapDifficultyAttributes? diffAttrs = null;

        byte[] image;
        try
        {
            image = await scoreTheme.RenderAsync(score, userInfo, mapBg, osuFilePath, diffAttrs, theme, heading);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render score image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }
}
