using System.Globalization;
using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using MintAPI.Rendering.MultiplayerTheme;
using MintAPI.Services;

namespace MintAPI.Controllers;

/// <summary>多人对局历史与玩家评分图。</summary>
[ApiController]
[Route("multiplayer")]
public class MultiplayerController(MultiplayerService multiplayer, MultiplayerTheme renderer,
    ILogger<MultiplayerController> logger) : ControllerBase
{
    /// <summary>渲染多人房已结束对局。</summary>
    /// <param name="mp_id">多人房 ID。</param>
    /// <param name="theme">渲染主题：default。</param>
    /// <param name="page">页码，从 1 开始；0 表示不分页，返回全部对局。</param>
    /// <param name="team_type">可选：head-to-head、team-vs、tag-coop 或 tag-team-vs。</param>
    /// <param name="trim">可选：开头局数,结尾局数。按队伍类型筛选后裁剪，默认 0,0。</param>
    /// <response code="200">PNG 图片，X-Page-Count 表示总页数。</response>
    /// <response code="400">参数无效或没有已结束的对局。</response>
    /// <response code="404">多人房不存在。</response>
    /// <response code="502">上游 API 读取失败。</response>
    [HttpGet("history")]
    [Produces("image/png", "application/problem+json")]
    public Task<IActionResult> GetMatchHistory([FromQuery] int? mp_id = null, [FromQuery] string theme = "default",
        [FromQuery] int page = 1, [FromQuery] string? team_type = null, [FromQuery] string? trim = null)
        => RenderAsync(mp_id, theme, page, team_type, null, trim);

    /// <summary>渲染玩家评分排名。</summary>
    /// <param name="mp_id">多人房 ID。</param>
    /// <param name="algorithm">osuplus、bathbot 或 flashlight，默认 osuplus。</param>
    /// <param name="theme">渲染主题：default。</param>
    /// <param name="page">页码，从 1 开始；0 表示不分页，返回全部玩家评分。</param>
    /// <param name="team_type">可选：head-to-head 或 team-vs；混合模式必须选择。</param>
    /// <param name="trim">可选：开头局数,结尾局数。按队伍类型筛选后裁剪，默认 0,0。</param>
    /// <response code="200">PNG 图片，X-Page-Count 表示总页数。</response>
    /// <response code="400">参数无效、没有有效成绩或模式不支持评分。</response>
    /// <response code="404">多人房不存在。</response>
    /// <response code="502">上游 API 读取失败。</response>
    [HttpGet("rating")]
    [Produces("image/png", "application/problem+json")]
    public Task<IActionResult> GetRating([FromQuery] int? mp_id = null, [FromQuery] string algorithm = "osuplus",
        [FromQuery] string theme = "default", [FromQuery] int page = 1, [FromQuery] string? team_type = null, [FromQuery] string? trim = null)
        => RenderAsync(mp_id, theme, page, team_type, algorithm.ToLowerInvariant(), trim);

    private async Task<IActionResult> RenderAsync(int? id, string theme, int page, string? teamType, string? algorithm, string? trim)
    {
        if (id is null or <= 0) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "mp_id 必须为正整数。");
        if (theme != "default") return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "theme 必须为 default。");
        if (page < 0)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "page 必须为非负整数，0 表示不分页。");
        if (algorithm != null && algorithm is not ("osuplus" or "bathbot" or "flashlight"))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "algorithm 必须为 osuplus、bathbot 或 flashlight。");
        // MVC binds both an omitted and a blank string query parameter to null.
        if (trim is null && !Request.Query.ContainsKey("trim")) trim = "0,0";
        var trimParts = trim?.Split(',');
        if (trimParts is not { Length: 2 } ||
            !int.TryParse(trimParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var trimStart) ||
            !int.TryParse(trimParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var trimEnd))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "trim 必须为两个逗号分隔的非负整数，例如 2,1。");
        if (teamType != null)
        {
            try { MultiplayerData.ParseTeamType(teamType); }
            catch (ArgumentException ex) { return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: ex.Message); }
        }
        MintOsuApi.Models.MatchResponse match;
        try
        {
            match = await multiplayer.GetCompleteMatchAsync(id.Value, HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ApiErrors.Result(ErrorCatalog.RecordNotFound, diagnostic: "多人房不存在。");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load multiplayer match {MatchId}", id);
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "读取多人房失败。");
        }
        try
        {
            var data = MultiplayerData.Build(match, teamType, trimStart, trimEnd);
            var pages = algorithm == null ? data.HistoryPages().Count : (data.Rate(algorithm).Count + 23) / 24;
            if (page > pages) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: $"page 必须为 0 或在 1 至 {pages} 之间。");
            var png = algorithm == null
                ? await renderer.RenderHistoryAsync(data, page, HttpContext.RequestAborted)
                : await renderer.RenderRatingAsync(data, algorithm, page, HttpContext.RequestAborted);
            Response.Headers["X-Page-Count"] = (page == 0 ? 1 : pages).ToString();
            Response.Headers["X-Page"] = page.ToString();
            return File(png, "image/png");
        }
        catch (ArgumentException ex) { return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: ex.Message); }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render multiplayer match {MatchId}", id);
            return ApiErrors.Result(ErrorCatalog.RenderFailed, diagnostic: "生成多人房图片失败。");
        }
    }
}
