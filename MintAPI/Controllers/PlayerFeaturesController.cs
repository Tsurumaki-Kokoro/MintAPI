using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Rendering.PlayerFeatures;
using MintAPI.Services;
using MintAPI.Services.PlayerFeatures;

namespace MintAPI.Controllers;

public sealed record SubscribePlayerRequest(
    [Required, StringLength(128)] string User,
    [StringLength(128)] string? Scope = null,
    [Range(0, 3)] int GameMode = 0,
    [StringLength(32)] string? Platform = null,
    [StringLength(80)] string? GroupId = null);

/// <summary>个人周报、同图成绩对比与纯文本玩家动态订阅。</summary>
[ApiController]
[ProducesResponseType(typeof(ApiError), 400, "application/problem+json")]
[ProducesResponseType(typeof(ApiError), 500, "application/problem+json")]
[ProducesResponseType(typeof(ApiError), 503, "application/problem+json")]
public sealed class PlayerFeaturesController(AppDbContext db, PlayerReportService reports,
    PlayerWatchService watches, PlayerFeatureRenderer renderer, IOptions<HistoryOptions> historyOptions,
    IOptions<PlayerWatchOptions> watchOptions, ILogger<PlayerFeaturesController> logger) : ControllerBase
{
    /// <summary>生成上一完整自然周（或指定周）的个人周报；缺失快照显示未知，不补造数据。</summary>
    /// <param name="platform">绑定平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="game_mode">模式 0–3，默认使用绑定模式。</param>
    /// <param name="week_start">该周周一，yyyy-MM-dd；默认上一完整自然周，不接受尚未结束的周。</param>
    /// <response code="200">PNG 周报。</response>
    /// <response code="400">参数无效或周期尚未结束；application/problem+json。</response>
    /// <response code="404">未绑定、玩家不存在或没有快照；application/problem+json。</response>
    /// <response code="500">渲染失败；application/problem+json。</response>
    /// <response code="502">osu! 服务失败；application/problem+json。</response>
    /// <response code="503">服务繁忙；application/problem+json。</response>
    [ProducesResponseType(typeof(byte[]), 200, "image/png")]
    [ProducesResponseType(typeof(ApiError), 404, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 502, "application/problem+json")]
    [HttpGet("user_info/weekly")]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> Weekly([FromQuery, Required] string platform, [FromQuery, Required] string platform_uid,
        [FromQuery, Range(0, 3)] int? game_mode = null, [FromQuery] DateOnly? week_start = null)
    {
        var ct = HttpContext.RequestAborted;
        var today = historyOptions.Value.Today(DateTimeOffset.UtcNow);
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var start = week_start ?? monday.AddDays(-7);
        if (start.DayOfWeek != DayOfWeek.Monday || start.Year < 2000 || start > monday.AddDays(-7))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, diagnostic: "week_start must be a completed Monday-based week since 2000");
        var binding = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Platform == platform && x.PlatformUid == platform_uid, ct);
        if (binding == null) return ApiErrors.Result(ErrorCatalog.UserNotBound);
        var mode = game_mode ?? binding.GameMode;
        if (mode is < 0 or > 3) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        var data = await reports.WeeklyAsync(binding.OsuUid, mode, start, ct);
        return await PngAsync(() => renderer.WeeklyAsync(data, ct));
    }

    /// <summary>对比两名玩家在同一谱面上的最佳匹配成绩，条件不同则停用优胜高亮。</summary>
    /// <param name="left_user">左侧 osu! ID 或用户名。</param>
    /// <param name="right_user">右侧 osu! ID 或用户名。</param>
    /// <param name="beatmap_id">具体难度 ID，不是谱面集 ID。</param>
    /// <param name="game_mode">模式 0–3。</param>
    /// <param name="mods">逗号分隔缩写，精确匹配组合；NM 为无 Mod。省略时各自取 PP 最高的成绩。</param>
    /// <param name="legacy_only">true 仅 Stable，false 仅 Lazer，省略时兼容两者。</param>
    /// <response code="200">PNG 同图对比。</response>
    /// <response code="400">参数无效；application/problem+json。</response>
    /// <response code="404">玩家、谱面或一方成绩不存在；application/problem+json。</response>
    /// <response code="500">渲染失败；application/problem+json。</response>
    /// <response code="502">上游失败；application/problem+json。</response>
    /// <response code="503">服务繁忙；application/problem+json。</response>
    [ProducesResponseType(typeof(byte[]), 200, "image/png")]
    [ProducesResponseType(typeof(ApiError), 404, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 502, "application/problem+json")]
    [HttpGet("score/compare")]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> Compare([FromQuery, Required, StringLength(128)] string left_user,
        [FromQuery, Required, StringLength(128)] string right_user, [FromQuery, Range(1, int.MaxValue)] int beatmap_id,
        [FromQuery, Range(0, 3)] int game_mode = 0, [FromQuery, StringLength(200)] string? mods = null,
        [FromQuery] bool? legacy_only = null)
    {
        if (string.IsNullOrWhiteSpace(left_user) || string.IsNullOrWhiteSpace(right_user) ||
            (Request.Query.ContainsKey("mods") && string.IsNullOrWhiteSpace(mods)) ||
            (mods != null && (string.IsNullOrWhiteSpace(mods) || mods.Split(',').Any(x =>
                x.Trim().Length is < 2 or > 4 || x.Trim().Any(c => !char.IsAsciiLetterOrDigit(c))))))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        var ct = HttpContext.RequestAborted;
        var data = await reports.CompareAsync(left_user, right_user, beatmap_id, game_mode, mods, legacy_only, ct);
        return await PngAsync(() => renderer.ComparisonAsync(data, ct));
    }

    /// <summary>建立或续期玩家动态订阅，首次仅建基线；相同玩家、模式、scope 返回已有订阅。</summary>
    /// <param name="request">osu! ID / 用户名和模式；群订阅提供 platform + groupId，旧调用方可继续提供 scope。</param>
    /// <response code="200">订阅 ID、初始 cursor 和过期时间。</response>
    /// <response code="400">参数无效；application/problem+json。</response>
    /// <response code="404">玩家不存在；application/problem+json。</response>
    /// <response code="409">订阅数量上限；application/problem+json。</response>
    /// <response code="502">上游失败；application/problem+json。</response>
    /// <response code="503">订阅服务关闭或繁忙；application/problem+json。</response>
    [ProducesResponseType(typeof(SubscriptionResult), 200, "application/json")]
    [ProducesResponseType(typeof(ApiError), 404, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 409, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 502, "application/problem+json")]
    [HttpPost("players/watch/subscriptions")]
    [Produces("application/json", "application/problem+json")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribePlayerRequest request)
    {
        if (!watchOptions.Value.Enabled)
        {
            Response.Headers.RetryAfter = "300";
            return ApiErrors.Result(ErrorCatalog.ServiceBusy);
        }
        if (string.IsNullOrWhiteSpace(request.User)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        var scope = request.Scope?.Trim();
        if (request.Platform != null || request.GroupId != null)
        {
            var group = PlayerWatchGroup.Create(request.Platform, request.GroupId);
            if (group == null || scope != null && scope != group.Scope) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
            scope = group.Scope;
        }
        if (string.IsNullOrWhiteSpace(scope)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        return Ok(await watches.SubscribeAsync(request.User.Trim(), request.GameMode, scope, HttpContext.RequestAborted));
    }

    /// <summary>分页查询订阅，可按群、scope、玩家和模式筛选；查询不续期、不请求 osu!。</summary>
    /// <param name="platform">平台，与 group_id 同时提供。</param>
    /// <param name="group_id">群号，与 platform 同时提供。</param>
    /// <param name="scope">旧调用方的精确 scope，不能同时使用群筛选。</param>
    /// <param name="user_id">osu! 数字 ID。</param>
    /// <param name="game_mode">模式 0–3，省略返回各模式。</param>
    /// <param name="include_expired">是否包含仍在数据库中的过期订阅，默认 false。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="page_size">每页 1–100 条，默认 50。</param>
    /// <response code="200">订阅列表及总数，无匹配时为空列表。</response>
    /// <response code="400">筛选参数无效；application/problem+json。</response>
    /// <response code="500">数据库读取失败；application/problem+json。</response>
    [HttpGet("players/watch/subscriptions")]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(PlayerSubscriptionPage), 200, "application/json")]
    public async Task<IActionResult> Subscriptions([FromQuery] string? platform = null, [FromQuery] string? group_id = null,
        [FromQuery, StringLength(128)] string? scope = null, [FromQuery, Range(1, int.MaxValue)] int? user_id = null,
        [FromQuery, Range(0, 3)] int? game_mode = null, [FromQuery] bool include_expired = false,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int page_size = 50)
    {
        if (!GroupFilter(platform, group_id, out var group) || (group != null && scope != null)
            || Request.Query.ContainsKey("scope") && string.IsNullOrWhiteSpace(scope)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        return Ok(await watches.ListAsync(group, scope, user_id, game_mode, include_expired, page, page_size, HttpContext.RequestAborted));
    }

    /// <summary>按平台和群分组列出订阅数量及玩家数，不请求 osu!、不续期。</summary>
    /// <param name="platform">可选平台筛选，例如 qq。</param>
    /// <param name="include_expired">是否计入仍保留的过期订阅。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="page_size">每页群数，1–100，默认 50。</param>
    /// <response code="200">群分组列表；任意非群 scope 不计入。</response>
    /// <response code="400">参数无效；application/problem+json。</response>
    /// <response code="500">数据库读取失败；application/problem+json。</response>
    [HttpGet("players/watch/groups")]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(PlayerGroupPage), 200, "application/json")]
    public async Task<IActionResult> Groups([FromQuery] string? platform = null, [FromQuery] bool include_expired = false,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int page_size = 50)
    {
        if (Request.Query.ContainsKey("platform") && string.IsNullOrWhiteSpace(platform)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        var normalized = platform == null ? null : PlayerWatchGroup.Create(platform, "validation");
        if (platform != null && normalized == null) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        return Ok(await watches.GroupsAsync(normalized?.Platform, include_expired, page, page_size, HttpContext.RequestAborted));
    }

    /// <summary>查询单条有效订阅及其群归属，可提供群身份校验；不续期。</summary>
    /// <param name="id">订阅 ID。</param>
    /// <param name="platform">可选平台，须与 group_id 同时提供。</param>
    /// <param name="group_id">可选群号，校验该订阅属于此群。</param>
    /// <response code="200">订阅详情，包含起点游标和最近采集时间。</response>
    /// <response code="400">群参数无效；application/problem+json。</response>
    /// <response code="404">订阅不属于指定群；application/problem+json。</response>
    /// <response code="410">订阅不存在或已过期；application/problem+json。</response>
    [HttpGet("players/watch/subscriptions/{id}")]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(PlayerSubscriptionInfo), 200, "application/json")]
    [ProducesResponseType(typeof(ApiError), 404, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 410, "application/problem+json")]
    public async Task<IActionResult> Subscription(string id, [FromQuery] string? platform = null, [FromQuery] string? group_id = null)
    {
        if (!GroupFilter(platform, group_id, out var group)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        return Ok(await watches.GetAsync(id, group, HttpContext.RequestAborted));
    }

    /// <summary>取消指定群内的订阅，可进一步指定玩家和模式；空匹配也成功，其他群不受影响。</summary>
    /// <param name="platform">平台，例如 qq。</param>
    /// <param name="group_id">群号。</param>
    /// <param name="user_id">可选 osu! ID；省略时取消该群所有玩家。</param>
    /// <param name="game_mode">可选模式；省略时取消所有模式。</param>
    /// <response code="200">实际删除的订阅数量。</response>
    /// <response code="400">群身份或筛选参数无效；application/problem+json。</response>
    /// <response code="503">订阅被并发修改，请重试；application/problem+json。</response>
    [HttpDelete("players/watch/subscriptions")]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(PlayerSubscriptionsRemoved), 200, "application/json")]
    public async Task<IActionResult> CancelGroup([FromQuery, Required] string platform, [FromQuery, Required] string group_id,
        [FromQuery, Range(1, int.MaxValue)] int? user_id = null, [FromQuery, Range(0, 3)] int? game_mode = null)
    {
        var group = PlayerWatchGroup.Create(platform, group_id);
        if (group == null) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        return Ok(new PlayerSubscriptionsRemoved(await watches.CancelGroupAsync(group, user_id, game_mode, HttpContext.RequestAborted)));
    }

    /// <summary>读取 cursor 之后的动态并续期；每条动态的 text 为可直接发送的纯文本。</summary>
    /// <param name="id">订阅 ID。</param>
    /// <param name="after">订阅返回或上次读取返回的 cursor；成功投递后保存新 cursor。</param>
    /// <param name="format">json 返回事件和游标；text 只返回纯文本，游标在 X-PlayerWatch-Cursor 响应头。</param>
    /// <response code="200">更新列表或 text/plain；无更新时列表为空或文本为空。</response>
    /// <response code="400">参数或游标无效；application/problem+json。</response>
    /// <response code="410">订阅或游标过期；application/problem+json。</response>
    /// <response code="500">读取失败；application/problem+json。</response>
    /// <response code="503">服务繁忙；application/problem+json。</response>
    [ProducesResponseType(typeof(WatchUpdates), 200, "application/json", "text/plain")]
    [ProducesResponseType(typeof(ApiError), 410, "application/problem+json")]
    [HttpGet("players/watch/subscriptions/{id}/updates")]
    [Produces("application/json", "text/plain", "application/problem+json")]
    public async Task<IActionResult> Updates(string id, [FromQuery, Range(typeof(long), "0", "9223372036854775807")] long after,
        [FromQuery] string format = "json")
    {
        if (format is not ("json" or "text") || id.Length != 32) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        var data = await watches.UpdatesAsync(id, after, HttpContext.RequestAborted);
        Response.Headers["X-PlayerWatch-Cursor"] = data.Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-PlayerWatch-Has-More"] = data.HasMore ? "true" : "false";
        return format == "text" ? Content(string.Join("\n\n", data.Updates.Select(x => x.Text)), "text/plain; charset=utf-8") : Ok(data);
    }

    /// <summary>取消玩家动态订阅；其他 scope 的订阅继续生效。</summary>
    /// <param name="id">订阅 ID。</param>
    /// <param name="platform">可选平台，须与 group_id 同时提供。</param>
    /// <param name="group_id">可选群号，校验后仅取消该群内的订阅。</param>
    /// <response code="400">群参数无效；application/problem+json。</response>
    /// <response code="404">订阅不属于指定群；application/problem+json。</response>
    /// <response code="204">取消成功。</response>
    /// <response code="410">订阅不存在或已过期；application/problem+json。</response>
    [ProducesResponseType(typeof(ApiError), 404, "application/problem+json")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiError), 410, "application/problem+json")]
    [HttpDelete("players/watch/subscriptions/{id}")]
    [Produces("application/problem+json")]
    public async Task<IActionResult> Unsubscribe(string id, [FromQuery] string? platform = null, [FromQuery] string? group_id = null)
    {
        if (!GroupFilter(platform, group_id, out var group)) return ApiErrors.Result(ErrorCatalog.InvalidArgument);
        await watches.UnsubscribeAsync(id, HttpContext.RequestAborted, group);
        return NoContent();
    }

    private bool GroupFilter(string? platform, string? groupId, out PlayerWatchGroup? group)
    {
        group = null;
        if (!Request.Query.ContainsKey("platform") && !Request.Query.ContainsKey("group_id")) return true;
        group = PlayerWatchGroup.Create(platform, groupId);
        return group != null;
    }

    private async Task<IActionResult> PngAsync(Func<Task<byte[]>> render)
    {
        try { return File(await render(), "image/png"); }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Player feature PNG rendering failed for {Path}", Request.Path);
            return ApiErrors.Result(ErrorCatalog.RenderFailed);
        }
    }
}
