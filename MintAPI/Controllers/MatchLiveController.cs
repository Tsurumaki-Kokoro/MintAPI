using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using MintAPI.Rendering.MultiplayerTheme;
using MintAPI.Services;
using MintAPI.Services.MatchLive;

namespace MintAPI.Controllers;

public sealed record CreateLiveSubscription(int MatchId, string Scope);

/// <summary>多人比赛实时订阅、增量事件与单局图片。</summary>
[ApiController]
[Route("multiplayer/live")]
public sealed class MatchLiveController(MatchLiveService live, MultiplayerTheme renderer,
    IHostEnvironment environment, ILogger<MatchLiveController> logger) : ControllerBase
{
    /// <summary>建立或续期订阅；相同比赛共享查询，相同 scope 重复订阅返回原 ID。</summary>
    /// <param name="request">比赛 ID 和调用方命名空间，例如 bot:qq:群号。</param>
    /// <response code="200">订阅 ID、当前快照和增量游标。</response>
    /// <response code="400">参数无效。</response>
    /// <response code="404">比赛不存在。</response>
    /// <response code="409">订阅或追踪数量达到上限。</response>
    [HttpPost("subscriptions")]
    [Produces("application/json", "application/problem+json")]
    public Task<IActionResult> Subscribe([FromBody] CreateLiveSubscription request)
        => RunAsync(async () =>
        {
            if (request.MatchId <= 0 || string.IsNullOrWhiteSpace(request.Scope) || request.Scope.Length > 128)
                return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "MatchId 必须为正整数，Scope 必须为 1–128 字符。");
            return Ok(await live.SubscribeAsync(request.MatchId, request.Scope.Trim(), HttpContext.RequestAborted));
        });

    /// <summary>取消订阅；无订阅时停止查询，短期保留比赛数据。</summary>
    /// <param name="id">订阅 ID。</param>
    /// <response code="204">取消成功。</response>
    /// <response code="410">订阅不存在或已过期。</response>
    [HttpDelete("subscriptions/{id}")]
    public Task<IActionResult> Unsubscribe(string id) => RunAsync(async () =>
    {
        await live.UnsubscribeAsync(id, HttpContext.RequestAborted);
        return NoContent();
    });

    /// <summary>读取 after 之后的更新并续期；保存返回的 cursor 可断线补取。</summary>
    /// <param name="id">订阅 ID。</param>
    /// <param name="after">MintAPI 更新游标，首次使用订阅返回的 cursor；不是 osu! event ID。</param>
    /// <response code="200">快照、更新列表和新 cursor；无变化时 updates 为空。</response>
    /// <response code="400">游标无效。</response>
    /// <response code="410">订阅或游标过期，需要重新订阅获取快照。</response>
    [HttpGet("subscriptions/{id}/updates")]
    [Produces("application/json", "application/problem+json")]
    public Task<IActionResult> Updates(string id, [FromQuery] long after = 0) => RunAsync(async () =>
        after < 0 ? BadRequest("after 必须非负。") : Ok(await live.UpdatesAsync(id, after, HttpContext.RequestAborted)));

    /// <summary>渲染已追踪比赛的指定局，进行中返回开局图，结束返回成绩图，无成绩返回中止图。</summary>
    /// <param name="mp_id">比赛 ID。</param>
    /// <param name="game_id">游戏 ID。</param>
    /// <response code="200">PNG 单局图片。</response>
    /// <response code="404">比赛未追踪或游戏不存在。</response>
    [HttpGet("{mp_id:int}/games/{game_id:int}/image")]
    [Produces("image/png", "application/problem+json")]
    public Task<IActionResult> GameImage(int mp_id, int game_id) => RunAsync(async () =>
    {
        var room = await live.GetRoomAsync(mp_id, HttpContext.RequestAborted);
        var game = room.Match.EventList.LastOrDefault(e => e.Game?.Id == game_id)?.Game;
        if (game is null) return ApiErrors.Result(ErrorCatalog.RecordNotFound, diagnostic: "没有该对局。");
        Response.Headers["X-MatchLive-Revision"] = room.Revision.ToString();
        Response.Headers["X-MatchLive-Mock"] = room.IsMock ? "true" : "false";
        return File(await renderer.RenderLiveGameAsync(room, game_id, HttpContext.RequestAborted), "image/png");
    });

    /// <summary>仅开发环境：将 109975520 的 mock 推进一阶段（等待→开局→结算→关闭）。</summary>
    /// <response code="200">模拟比赛快照。</response>
    /// <response code="404">非开发环境、mock 未启用或未订阅。</response>
    [HttpPost("mock/109975520/advance")]
    [Produces("application/json", "application/problem+json")]
    public Task<IActionResult> AdvanceMock() => RunAsync(async () =>
        !environment.IsDevelopment() ? NotFound() : Ok(await live.AdvanceMockAsync(HttpContext.RequestAborted)));

    /// <summary>仅开发环境：重新开始 mock 重放，订阅和更新游标保持有效。</summary>
    /// <response code="200">重置后的模拟快照。</response>
    /// <response code="404">非开发环境、mock 未启用或未订阅。</response>
    /// <response code="502">上游服务查询失败；返回统一错误 JSON。</response>
    [HttpPost("mock/109975520/reset")]
    [Produces("application/json", "application/problem+json")]
    public Task<IActionResult> ResetMock() => RunAsync(async () =>
        !environment.IsDevelopment() ? NotFound() : Ok(await live.ResetMockAsync(HttpContext.RequestAborted)));

    private async Task<IActionResult> RunAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (LiveConflictException ex) { return ApiErrors.Result(ErrorCatalog.Conflict, diagnostic: ex.Message); }
        catch (LiveGoneException ex) { return ApiErrors.Result(ErrorCatalog.ResourceExpired, diagnostic: ex.Message); }
        catch (ArgumentException ex) { return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: ex.Message); }
        catch (KeyNotFoundException ex) { return ApiErrors.Result(ErrorCatalog.RecordNotFound, diagnostic: ex.Message); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return ApiErrors.Result(ErrorCatalog.RecordNotFound, diagnostic: "比赛不存在。"); }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        { return ApiErrors.Result(ErrorCatalog.Forbidden, diagnostic: "比赛不可访问。"); }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Match live request failed");
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "比赛查询或实时服务暂时不可用。");
        }
    }
}
