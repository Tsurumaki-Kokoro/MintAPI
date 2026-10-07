using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using MintAPI.Services.Preview;

namespace MintAPI.Controllers;

/// <summary>谱面 GIF、PNG 与带音频视频预览。</summary>
[ApiController]
[Route("beatmap/preview")]
public sealed class BeatmapPreviewController(IBeatmapPreviewService preview,
    ILogger<BeatmapPreviewController> logger) : ControllerBase
{
    /// <summary>生成谱面 GIF 动图或 PNG 静态预览。</summary>
    /// <param name="beatmap_id">正整数谱面 ID。</param>
    /// <param name="format">gif（默认）或 png。</param>
    /// <param name="mods">Mod 数组；重复 mods 参数，每项一个 token。</param>
    /// <param name="convert">Standard 转谱目标：standard、taiko、ctb 或 mania。</param>
    /// <param name="time_points">起点秒数或 preview；GIF/Standard PNG 最多四项。</param>
    /// <param name="duration">GIF 每片段时长，默认 6 秒，最多 12 秒；非 Standard PNG 区间最多 60 秒。</param>
    /// <param name="selection">auto（默认）：四模式原生 GIF 为预览时间加三个难段；hardest 为四个难段。手动时间点优先。</param>
    /// <response code="200">GIF 或 PNG 文件。</response>
    /// <response code="400">参数组合无效。</response>
    /// <response code="502">预览引擎或依赖失败。</response>
    /// <response code="503">繁忙或超时，可重试。</response>
    [HttpGet("image")]
    [Produces("image/gif", "image/png", "application/json", "application/problem+json")]
    public Task<IActionResult> GetImage([FromQuery] int beatmap_id,
        [FromQuery] string format = "gif", [FromQuery] string[]? mods = null,
        [FromQuery] string? convert = null, [FromQuery] string[]? time_points = null,
        [FromQuery] double? duration = null, [FromQuery] string selection = "auto")
    {
        if (!string.Equals(format, "gif", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "format must be gif or png." }));
        return GenerateAsync(new(beatmap_id, format, convert, mods ?? [], time_points ?? [], duration, selection));
    }

    /// <summary>同步生成带谱面音频的 MP4；支持 Range 请求。</summary>
    /// <param name="beatmap_id">正整数谱面 ID。</param>
    /// <param name="mods">Mod 数组；重复 mods 参数，每项一个 token。</param>
    /// <param name="convert">Standard 转谱目标：standard、taiko、ctb 或 mania。</param>
    /// <param name="start">游戏时间轴起点秒数或 preview，默认 preview。</param>
    /// <param name="duration">时长，默认 30 秒，最多 60 秒。</param>
    /// <response code="200">带音频 MP4。</response>
    /// <response code="206">MP4 部分内容。</response>
    /// <response code="400">参数组合无效。</response>
    /// <response code="502">预览引擎或依赖失败。</response>
    /// <response code="503">繁忙或超时，可重试。</response>
    [HttpGet("video")]
    [Produces("video/mp4", "application/json", "application/problem+json")]
    public Task<IActionResult> GetVideo([FromQuery] int beatmap_id,
        [FromQuery] string[]? mods = null, [FromQuery] string? convert = null,
        [FromQuery] string start = "preview", [FromQuery] double duration = 30) =>
        GenerateAsync(new(beatmap_id, "mp4", convert, mods ?? [], [start], duration));

    private async Task<IActionResult> GenerateAsync(BeatmapPreviewRequest request)
    {
        try
        {
            var result = await preview.GenerateAsync(request, HttpContext.RequestAborted);
            return PhysicalFile(result.Path, result.ContentType, enableRangeProcessing: request.Format == "mp4");
        }
        catch (PreviewValidationException ex) { return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: ex.Message); }
        catch (PreviewFailedException ex)
        {
            logger.LogWarning(ex, "Preview failed for {BeatmapId}", request.BeatmapId);
            return ApiErrors.Result(ErrorCatalog.PreviewUnavailable, diagnostic: "Preview service unavailable or generation failed");
        }
    }
}
