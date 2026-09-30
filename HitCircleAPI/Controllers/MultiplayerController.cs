using HitCircleAPI.Rendering.MultiplayerTheme;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace HitCircleAPI.Controllers;

/// <summary>多人对局历史与玩家评分图。</summary>
[ApiController]
[Route("multiplayer")]
public class MultiplayerController(MultiplayerService multiplayer, MultiplayerTheme renderer,
    ILogger<MultiplayerController> logger) : ControllerBase
{
    /// <summary>渲染多人房逐局历史，按完整对局分页；仅展示已结束的对局。</summary>
    /// <param name="mp_id">必填，正整数多人房 ID。</param>
    /// <param name="theme">apple 或 default，均使用苹果风格模板。</param>
    /// <param name="page">图片页码，从 1 开始；总页数见 X-Page-Count 响应头。</param>
    /// <param name="team_type">可选：head-to-head、team-vs、tag-coop 或 tag-team-vs。</param>
    /// <response code="200">PNG 图片，X-Page-Count 表示总页数。</response>
    /// <response code="400">参数无效或没有已结束的对局。</response>
    /// <response code="404">多人房不存在。</response>
    /// <response code="502">上游 API 读取失败。</response>
    [HttpGet("history")]
    [Produces("image/png")]
    public Task<IActionResult> GetMatchHistory([FromQuery] int? mp_id = null, [FromQuery] string theme = "default",
        [FromQuery] int page = 1, [FromQuery] string? team_type = null)
        => RenderAsync(mp_id, theme, page, team_type, null);

    /// <summary>渲染玩家评分排名。零分不参与评分；平局单列，个人并列第一均计为第一。</summary>
    /// <param name="mp_id">必填，正整数多人房 ID。</param>
    /// <param name="algorithm">osuplus、bathbot 或 flashlight，默认 osuplus。</param>
    /// <param name="theme">apple 或 default，均使用苹果风格模板。</param>
    /// <param name="page">图片页码，从 1 开始，每页最多 24 位玩家。</param>
    /// <param name="team_type">可选：head-to-head 或 team-vs；混合模式必须选择。</param>
    /// <response code="200">PNG 图片，X-Page-Count 表示总页数。</response>
    /// <response code="400">参数无效、没有有效成绩或模式不支持评分。</response>
    /// <response code="404">多人房不存在。</response>
    /// <response code="502">上游 API 读取失败。</response>
    [HttpGet("rating")]
    [Produces("image/png")]
    public Task<IActionResult> GetRating([FromQuery] int? mp_id = null, [FromQuery] string algorithm = "osuplus",
        [FromQuery] string theme = "default", [FromQuery] int page = 1, [FromQuery] string? team_type = null)
        => RenderAsync(mp_id, theme, page, team_type, algorithm.ToLowerInvariant());

    private async Task<IActionResult> RenderAsync(int? id, string theme, int page, string? teamType, string? algorithm)
    {
        if (id is null or <= 0) return BadRequest("mp_id 必须为正整数。");
        if (theme is not ("default" or "apple")) return BadRequest("theme 必须为 apple 或 default。");
        if (page < 1) return BadRequest("page 必须为正整数。");
        if (algorithm != null && algorithm is not ("osuplus" or "bathbot" or "flashlight"))
            return BadRequest("algorithm 必须为 osuplus、bathbot 或 flashlight。");
        if (teamType != null)
        {
            try { MultiplayerData.ParseTeamType(teamType); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
        }
        Ossapi.Models.MatchResponse match;
        try
        {
            match = await multiplayer.GetCompleteMatchAsync(id.Value, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound("多人房不存在。");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load multiplayer match {MatchId}", id);
            return StatusCode(502, "读取多人房失败。");
        }
        try
        {
            var data = MultiplayerData.Build(match, teamType);
            var pages = algorithm == null ? data.HistoryPages().Count : (data.Rate(algorithm).Count + 23) / 24;
            if (page > pages) return BadRequest($"page 必须在 1 至 {pages} 之间。");
            var png = algorithm == null
                ? await renderer.RenderHistoryAsync(data, page, HttpContext.RequestAborted)
                : await renderer.RenderRatingAsync(data, algorithm, page, HttpContext.RequestAborted);
            Response.Headers["X-Page-Count"] = pages.ToString();
            Response.Headers["X-Page"] = page.ToString();
            return File(png, "image/png");
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render multiplayer match {MatchId}", id);
            return StatusCode(500, "生成多人房图片失败。");
        }
    }
}
