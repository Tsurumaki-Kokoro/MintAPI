using HitCircleAPI.Data;
using HitCircleAPI.Rendering.ScoreTheme;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ossapi.Enums;
using Ossapi.Models;

namespace HitCircleAPI.Controllers;

/// <summary>成绩图：最近游玩、最好成绩、指定谱面成绩。</summary>
[ApiController]
[Route("score")]
public class ScoreController(
    AppDbContext db,
    IOsuApiService osuApi,
    IBeatmapFileService beatmapFile,
    DefaultScoreTheme scoreTheme,
    ILogger<ScoreController> logger) : ControllerBase
{
    /// <summary>渲染最近一次游玩的成绩图。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="game_mode">游戏模式（0=osu!，1=taiko，2=catch，3=mania），默认沿用绑定时的模式。</param>
    /// <param name="include_fails">是否把失败的成绩也算进最近游玩。</param>
    /// <param name="theme">渲染主题，默认 default。</param>
    /// <response code="200">渲染好的 PNG 成绩图。</response>
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
        [FromQuery] string theme = "default")
    {
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
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Recent, mode, limit: 1);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get recent scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No recent play record found");

        return await RenderScoreAsync(scores[0], userInfo, mode);
    }

    /// <summary>渲染第 N 个最好成绩（BP）的成绩图。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="game_mode">游戏模式（0=osu!，1=taiko，2=catch，3=mania），默认沿用绑定时的模式。</param>
    /// <param name="best_index">第几个 BP，从 1 开始。</param>
    /// <param name="theme">渲染主题，默认 default。</param>
    /// <response code="200">渲染好的 PNG 成绩图。</response>
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
        [FromQuery] string theme = "default")
    {
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
            scores = await osuApi.GetUserScoresAsync(userInfo.Id, ScoreType.Best, mode, limit: 1, offset: best_index - 1);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get best scores for user {UserId}", userInfo.Id);
            return BadRequest($"Failed to get scores: {ex.Message}");
        }

        if (scores.Count == 0)
            return NotFound("No best play record found");

        return await RenderScoreAsync(scores[0], userInfo, mode);
    }

    /// <summary>渲染指定用户在指定谱面上的成绩图。</summary>
    /// <param name="platform">平台标识，如 qq、discord。</param>
    /// <param name="platform_uid">该平台上的用户 ID。</param>
    /// <param name="beatmap_id">谱面 ID。</param>
    /// <param name="game_mode">游戏模式（0=osu!，1=taiko，2=catch，3=mania），默认沿用绑定时的模式。</param>
    /// <param name="theme">渲染主题，默认 default。</param>
    /// <response code="200">渲染好的 PNG 成绩图。</response>
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
            userScores = await osuApi.GetBeatmapUserScoresAsync(beatmap_id, userInfo.Id, mode);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get user score for beatmap {BeatmapId}", beatmap_id);
            return BadRequest($"Failed to get user score: {ex.Message}");
        }

        if (userScores.Count == 0)
            return NotFound("No score found for this beatmap");

        return await RenderScoreAsync(userScores[0], userInfo, mode);
    }

    private async Task<IActionResult> RenderScoreAsync(Score score, User userInfo, GameMode? mode)
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
        try
        {
            var mods = score.Mods?.Where(m => m.Acronym != "CL").Select(m => m.Acronym).ToList() ?? [];
            // Build mod flags string for ossapi
            var modParam = mods.Count > 0 ? string.Join("", mods) : null;
            // Note: diffAttrs fetch is best-effort; null is handled in theme renderer
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogWarning(ex, "Failed to get beatmap attributes for {BeatmapId}, proceeding without", beatmap.Id);
        }

        byte[] image;
        try
        {
            image = await scoreTheme.RenderAsync(score, userInfo, mapBg, osuFilePath, diffAttrs);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render score image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }
}
