using Microsoft.AspNetCore.Mvc;
using MintAPI.Rendering.BeatmapTheme;
using MintAPI.Services;
using MintOsuApi.Models;

namespace MintAPI.Controllers;

/// <summary>谱面封面与谱面信息图。</summary>
[ApiController]
[Route("beatmap")]
public class BeatmapController(
    IOsuApiService osuApi,
    IBeatmapFileService beatmapFile,
    DefaultBeatmapTheme beatmapTheme,
    ILogger<BeatmapController> logger) : ControllerBase
{
    /// <summary>取谱面背景原图。</summary>
    /// <param name="beatmap_id">谱面 ID，与 beatmapset_id 二选一。</param>
    /// <param name="beatmapset_id">谱面集 ID；取首张谱面背景。</param>
    /// <response code="200">谱面背景图（JPEG）。</response>
    /// <response code="400">两个 ID 都没给。</response>
    /// <response code="500">读取失败。</response>
    [HttpGet("cover")]
    [Produces("image/jpeg")]
    public async Task<IActionResult> GetBeatmapCover(
        [FromQuery] int? beatmap_id = null,
        [FromQuery] int? beatmapset_id = null)
    {
        if (beatmap_id is null && beatmapset_id is null)
            return BadRequest("Either beatmap_id or beatmapset_id is required");

        Beatmapset beatmapsetInfo;
        int resolvedBeatmapId;
        try
        {
            if (beatmap_id.HasValue)
            {
                var beatmap = await osuApi.GetBeatmapAsync(beatmap_id.Value);
                beatmapsetInfo = await osuApi.GetBeatmapsetAsync(beatmap.BeatmapsetId);
                resolvedBeatmapId = beatmap_id.Value;
            }
            else
            {
                beatmapsetInfo = await osuApi.GetBeatmapsetAsync(beatmapset_id!.Value);
                resolvedBeatmapId = beatmapsetInfo.Beatmaps?.FirstOrDefault()?.Id ?? 0;
            }
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get beatmapset info");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        if (resolvedBeatmapId == 0)
            return StatusCode(500, "Cannot determine beatmap ID");

        string osuFilePath;
        try
        {
            osuFilePath = await beatmapFile.GetOsuFilePathAsync(beatmapsetInfo.Id, resolvedBeatmapId);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get osu file");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        var bgName = beatmapFile.GetBgFilename(osuFilePath);
        byte[] cover;
        try
        {
            cover = await beatmapFile.GetMapBgAsync(beatmapsetInfo.Id, resolvedBeatmapId, bgName);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get map background");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(cover, "image/jpeg");
    }

    /// <summary>渲染谱面信息图。default 包含各模式的 PP 参考、局部难度曲线与 Mods 对比，并展示 Standard / Taiko 的原生难度分项及 Catch / Mania 的物件构成。</summary>
    /// <param name="beatmap_id">谱面 ID，必须为正整数。</param>
    /// <param name="theme">渲染主题：default 或 yaowan（原 Python 模板）。</param>
    /// <response code="200">PNG 图片。</response>
    /// <response code="400">参数或谱面无效。</response>
    /// <response code="500">读取或渲染失败。</response>
    [HttpGet("beatmap")]
    [Produces("image/png")]
    public async Task<IActionResult> GetBeatmapInfo(
        [FromQuery] int? beatmap_id = null,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "yaowan")) return BadRequest("theme 必须为 default 或 yaowan。");
        if (beatmap_id is null or <= 0)
            return BadRequest("beatmap_id 必须为正整数。");

        return await RenderBeatmapInfoAsync(beatmap_id.Value, theme);
    }

    /// <summary>渲染谱面集信息图，展示谱面集资料与各难度列表。</summary>
    /// <param name="beatmapset_id">谱面集 ID，必须为正整数。</param>
    /// <param name="theme">渲染主题：default 或 yaowan（原 Python 模板）。</param>
    /// <response code="200">PNG 图片。</response>
    /// <response code="400">参数或谱面集无效。</response>
    /// <response code="500">读取或渲染失败。</response>
    [HttpGet("beatmapset")]
    [Produces("image/png")]
    public async Task<IActionResult> GetBeatmapsetInfo(
        [FromQuery] int? beatmapset_id = null,
        [FromQuery] string theme = "default")
    {
        if (theme is not ("default" or "yaowan")) return BadRequest("theme 必须为 default 或 yaowan。");
        if (beatmapset_id is null or <= 0)
            return BadRequest("beatmapset_id 必须为正整数。");

        return await RenderBeatmapsetInfoAsync(beatmapset_id.Value, theme);
    }

    private async Task<IActionResult> RenderBeatmapInfoAsync(int beatmapId, string theme)
    {
        Beatmap beatmap;
        try
        {
            beatmap = await osuApi.GetBeatmapAsync(beatmapId);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get beatmap {BeatmapId}", beatmapId);
            return BadRequest($"Failed to get beatmap: {ex.Message}");
        }

        var beatmapsetId = beatmap.BeatmapsetId;

        string osuFilePath;
        try
        {
            osuFilePath = await beatmapFile.GetOsuFilePathAsync(beatmapsetId, beatmapId);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get osu file for beatmap {BeatmapId}", beatmapId);
            return StatusCode(500, "Failed to get beatmap file");
        }

        User mapper;
        try
        {
            var mapperId = beatmap.Beatmapset?.UserId ?? 0;
            if (mapperId == 0)
            {
                var fullSet = await osuApi.GetBeatmapsetAsync(beatmapsetId);
                beatmap.Beatmapset = fullSet;
                mapperId = fullSet.UserId;
            }
            mapper = await osuApi.GetUserAsync(mapperId.ToString());
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get mapper info for beatmap {BeatmapId}", beatmapId);
            return StatusCode(500, "Failed to get mapper info");
        }

        var bgName = beatmapFile.GetBgFilename(osuFilePath);
        byte[] mapBg;
        try
        {
            mapBg = await beatmapFile.GetMapBgAsync(beatmapsetId, beatmapId, bgName);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get map background for beatmap {BeatmapId}", beatmapId);
            return StatusCode(500, "Failed to get map background");
        }

        byte[] image;
        try
        {
            image = await beatmapTheme.RenderBeatmapAsync(beatmap, mapper, mapBg, osuFilePath, theme);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render beatmap image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }

    private async Task<IActionResult> RenderBeatmapsetInfoAsync(int beatmapsetId, string theme)
    {
        Beatmapset beatmapset;
        try
        {
            beatmapset = await osuApi.GetBeatmapsetAsync(beatmapsetId);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get beatmapset {BeatmapsetId}", beatmapsetId);
            return BadRequest($"Failed to get beatmapset: {ex.Message}");
        }

        var firstMap = beatmapset.Beatmaps?.FirstOrDefault();
        if (firstMap is null)
            return StatusCode(500, "Beatmapset has no beatmaps");

        string osuFilePath;
        try
        {
            osuFilePath = await beatmapFile.GetOsuFilePathAsync(beatmapsetId, firstMap.Id);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get osu file for beatmapset {BeatmapsetId}", beatmapsetId);
            return StatusCode(500, "Failed to get beatmap file");
        }

        var bgName = beatmapFile.GetBgFilename(osuFilePath);
        byte[] coverBg;
        try
        {
            coverBg = await beatmapFile.GetMapBgAsync(beatmapsetId, firstMap.Id, bgName);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to get cover for beatmapset {BeatmapsetId}", beatmapsetId);
            return StatusCode(500, "Failed to get beatmapset cover");
        }

        byte[] image;
        try
        {
            image = await beatmapTheme.RenderBeatmapsetAsync(beatmapset, coverBg, theme);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render beatmapset image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }
}
