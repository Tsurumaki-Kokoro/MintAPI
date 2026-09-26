using HitCircleAPI.Rendering.BeatmapTheme;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Ossapi.Models;

namespace HitCircleAPI.Controllers;

[ApiController]
[Route("beatmap")]
public class BeatmapController(
    IOsuApiService osuApi,
    IBeatmapFileService beatmapFile,
    DefaultBeatmapTheme beatmapTheme,
    ILogger<BeatmapController> logger) : ControllerBase
{
    [HttpGet("cover")]
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

    [HttpGet("info")]
    public async Task<IActionResult> GetBeatmapInfo(
        [FromQuery] int? beatmap_id = null,
        [FromQuery] int? beatmapset_id = null,
        [FromQuery] string theme = "default")
    {
        if (beatmap_id is null && beatmapset_id is null)
            return BadRequest("Either beatmap_id or beatmapset_id is required");

        if (beatmap_id.HasValue)
        {
            return await RenderBeatmapInfoAsync(beatmap_id.Value);
        }
        else
        {
            return await RenderBeatmapsetInfoAsync(beatmapset_id!.Value);
        }
    }

    private async Task<IActionResult> RenderBeatmapInfoAsync(int beatmapId)
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
            image = await beatmapTheme.RenderBeatmapAsync(beatmap, mapper, mapBg, osuFilePath);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render beatmap image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }

    private async Task<IActionResult> RenderBeatmapsetInfoAsync(int beatmapsetId)
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
            image = await beatmapTheme.RenderBeatmapsetAsync(beatmapset, coverBg);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to render beatmapset image");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }

        return File(image, "image/png");
    }
}
