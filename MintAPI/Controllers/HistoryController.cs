using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Services;
using MintOsuApi.Enums;

namespace MintAPI.Controllers;

/// <summary>玩家 PP/排名历史和指定谱面成绩列表。</summary>
[ApiController]
public sealed class HistoryController(AppDbContext db, IOsuApiService osuApi, HistoryService history,
    HistoryRenderer renderer) : ControllerBase
{
    /// <summary>查询每日 PP/全球排名趋势，本地与 osu!track 同日期数据优先使用本地。</summary>
    /// <param name="platform">绑定平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="game_mode">模式 0–3，省略则使用绑定模式。</param>
    /// <param name="days">最近天数；0 返回全部本地历史，外部历史使用配置的默认天数。</param>
    /// <param name="format">json 或 png。</param>
    /// <response code="200">历史数据或 PNG 趋势图。</response>
    /// <response code="400">参数无效。</response>
    /// <response code="404">用户未绑定或没有历史数据。</response>
    [HttpGet("user_info/history")]
    [Produces("application/json", "image/png", "application/problem+json")]
    public async Task<IActionResult> UserHistory([FromQuery] string platform, [FromQuery] string platform_uid,
        [FromQuery] int? game_mode = null, [FromQuery] int days = 0, [FromQuery] string format = "json")
    {
        if (game_mode is < 0 or > 3 || days is < 0 or > 3650 || format is not ("json" or "png")) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid mode, days or format");
        var ct = HttpContext.RequestAborted;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid, ct);
        if (user == null) return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");
        var mode = game_mode ?? user.GameMode;
        if (mode is < 0 or > 3) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid bound game mode");
        var points = await history.GetUserHistoryAsync(user.OsuUid, mode, days, ct);
        if (points.Count == 0) return ApiErrors.Result(ErrorCatalog.HistoryNotFound, diagnostic: "No history found");
        if (format == "json") return Ok(new { user_id = user.OsuUid, game_mode = mode, sources = points.Select(p => p.Source).Distinct(), points });
        return File(await renderer.RenderTrendAsync($"osu! {user.OsuUid} · {(GameMode)mode} · PP / Rank", points, ct), "image/png");
    }

    /// <summary>查询谱面成绩列表：有榜谱面使用官网最佳成绩，无榜谱面使用本地采集历史。</summary>
    /// <param name="platform">绑定平台。</param>
    /// <param name="platform_uid">平台用户 ID。</param>
    /// <param name="beatmap_id">谱面 ID。</param>
    /// <param name="game_mode">模式 0–3，省略则使用绑定模式。</param>
    /// <param name="mods">逗号分隔的 Mod 缩写，精确匹配组合；NM 表示无 Mod，忽略 CL。</param>
    /// <param name="legacy_only">只显示 Stable 成绩。</param>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="page_size">每页 1–50 条，默认 20。</param>
    /// <param name="format">json 或 png。</param>
    /// <response code="200">分页成绩数据或 PNG 列表。</response>
    /// <response code="400">参数无效。</response>
    /// <response code="404">用户未绑定或没有成绩。</response>
    [HttpGet("score/history")]
    [Produces("application/json", "image/png", "application/problem+json")]
    public async Task<IActionResult> ScoreHistory([FromQuery] string platform, [FromQuery] string platform_uid,
        [FromQuery] int beatmap_id, [FromQuery] int? game_mode = null, [FromQuery] string? mods = null,
        [FromQuery] bool legacy_only = false, [FromQuery] int page = 1, [FromQuery] int page_size = 20,
        [FromQuery] string format = "json")
    {
        if (beatmap_id <= 0 || game_mode is < 0 or > 3 || page is < 1 or > 1000000 || page_size is < 1 or > 50 || format is not ("json" or "png"))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid query parameters");
        var ct = HttpContext.RequestAborted;
        var binding = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Platform == platform && u.PlatformUid == platform_uid, ct);
        if (binding == null) return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User not found");
        var mode = game_mode ?? binding.GameMode;
        if (mode is < 0 or > 3) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid bound game mode");
        var user = await osuApi.GetUserAsync(binding.OsuUid, (GameMode)mode).WaitAsync(ct);
        var result = await history.GetMapScoresAsync(user.Id, beatmap_id, mode, ct);
        var scores = result.Scores.AsEnumerable();
        if (legacy_only) scores = scores.Where(s => s.LegacyScoreId is > 0);
        if (mods != null)
        {
            var requested = mods.ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(m => m is not ("NM" or "CL")).ToHashSet();
            scores = scores.Where(s => requested.SetEquals(s.Mods?.Where(m => m.Acronym != "CL").Select(m => m.Acronym) ?? []));
        }
        var ordered = scores.OrderByDescending(s => s.EndedAt).ToList();
        var offset = (page - 1) * page_size;
        var items = ordered.Skip(offset).Take(page_size).ToList();
        if (items.Count == 0)
            return ordered.Count > 0
                ? ApiErrors.Result(ErrorCatalog.ScorePageNotFound)
                : result.Source == "local" && result.Scores.Count == 0
                    ? ApiErrors.Result(ErrorCatalog.LocalScoreNotCollected)
                    : ApiErrors.Result(ErrorCatalog.ScoreNotFound);
        if (format == "json") return Ok(new { user_id = user.Id, beatmap_id, game_mode = mode, source = result.Source, notice = result.Notice, total = ordered.Count, page, page_size, scores = items });
        return File(await renderer.RenderScoresAsync($"{user.Username} · Beatmap {beatmap_id} · {(GameMode)mode}", result.Notice, items, offset, ordered.Count, ct), "image/png");
    }
}
