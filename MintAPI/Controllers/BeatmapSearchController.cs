using MintAPI.Configuration;
using Microsoft.Extensions.Options;
using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MintAPI.Services;
using MintOsuApi;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Controllers;

/// <summary>通过 osu! API 搜索谱面集。</summary>
[ApiController]
[Route("beatmap/search")]
public sealed class BeatmapSearchController(
    IOsuApiService osuApi,
    IMemoryCache cache,
    ILogger<BeatmapSearchController> logger, IOptions<CachePolicyOptions>? cachePolicy = null) : ControllerBase
{
    /// <summary>搜索谱面集并返回紧凑列表 PNG；每张图片最多显示 5 个谱面集。</summary>
    /// <param name="theme">搜索图片渲染器。</param>
    /// <param name="query">关键词或官方搜索表达式，必填。</param>
    /// <param name="mode">any、osu、taiko、catch 或 mania。</param>
    /// <param name="status">谱面状态，默认 any。</param>
    /// <param name="sort">官方排序值，例如 relevance_desc。</param>
    /// <param name="cursor_string">官方批次游标；取自 JSON 响应或 X-Next-Cursor 响应头。</param>
    /// <param name="page">当前官方搜索批次内的图片页码，从 1 开始。</param>
    /// <param name="ct">请求取消令牌。</param>
    /// <response code="200">PNG 图片；X-Page-Count 为本批图片页数，X-Next-Cursor 用于下一批。</response>
    /// <response code="400">搜索参数或图片页码无效。</response>
    /// <response code="502">osu! 搜索失败。</response>
    /// <response code="503">资源繁忙。</response>
    [HttpGet("image")]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> SearchImage(
        [FromServices] MintAPI.Rendering.BeatmapSearchTheme.BeatmapSearchTheme theme,
        [FromQuery] string? query = null, [FromQuery] string mode = "any",
        [FromQuery] string status = "any", [FromQuery] string? sort = null,
        [FromQuery] string? cursor_string = null, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page < 1) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "page 必须大于等于 1。");
        var response = await Search(query, mode, status, sort, cursor_string, ct);
        if (response is not ContentResult content) return response;
        var result = JsonConvert.DeserializeObject<BeatmapsetSearchResult>(content.Content!, OsuClient.BuildJsonSettings())!;
        var pages = Math.Max(1, (result.Beatmapsets.Count + 4) / 5);
        if (page > pages) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: $"本批搜索结果只有 {pages} 页。");
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Count"] = pages.ToString();
        Response.Headers["X-Total"] = result.Total.ToString();
        if (!string.IsNullOrEmpty(result.CursorString)) Response.Headers["X-Next-Cursor"] = result.CursorString;
        var sets = result.Beatmapsets.Skip((page - 1) * 5).Take(5).ToArray();
        return File(await theme.RenderAsync(sets, query!.Trim(), mode, status, page, pages, result.Total, ct), "image/png");
    }

    private static readonly Dictionary<string, BeatmapsetSearchCategory> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["any"] = BeatmapsetSearchCategory.Any,
        ["leaderboard"] = BeatmapsetSearchCategory.HasLeaderboard,
        ["ranked"] = BeatmapsetSearchCategory.Ranked,
        ["qualified"] = BeatmapsetSearchCategory.Qualified,
        ["loved"] = BeatmapsetSearchCategory.Loved,
        ["pending"] = BeatmapsetSearchCategory.Pending,
        ["wip"] = BeatmapsetSearchCategory.Wip,
        ["graveyard"] = BeatmapsetSearchCategory.Graveyard,
    };

    private static readonly Dictionary<string, BeatmapsetSearchSort> Sorts = Enum.GetValues<BeatmapsetSearchSort>()
        .ToDictionary(value => value.ToString().Replace("Descending", "_desc").Replace("Ascending", "_asc")
            .Replace("Favorites", "Favourites").ToLowerInvariant(), value => value, StringComparer.OrdinalIgnoreCase);

    /// <summary>搜索谱面集，返回 JSON；每个谱面集包含其具体难度。</summary>
    /// <param name="query">歌名、艺术家、谱师或官方搜索表达式；必填，最多 500 字符。</param>
    /// <param name="mode">any、osu、taiko、catch 或 mania；默认 any。</param>
    /// <param name="status">any、leaderboard、ranked、qualified、loved、pending、wip 或 graveyard。</param>
    /// <param name="sort">官方排序值，例如 relevance_desc、title_asc、difficulty_desc、ranked_desc、plays_desc；省略时使用官方默认排序。</param>
    /// <param name="cursor_string">上次响应的 cursor_string；翻页时保持其他搜索条件不变。</param>
    /// <param name="ct">请求取消令牌。</param>
    /// <response code="200">搜索结果；total 为谱面集总数，beatmapsets 为本页谱面集，cursor_string 用于下一页。</response>
    /// <response code="400">搜索参数无效。</response>
    /// <response code="502">osu! API 请求失败。</response>
    /// <response code="503">osu! API 配额不足，请稍后重试。</response>
    [HttpGet]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(BeatmapsetSearchResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? query = null,
        [FromQuery] string mode = "any",
        [FromQuery] string status = "any",
        [FromQuery] string? sort = null,
        [FromQuery] string? cursor_string = null,
        CancellationToken ct = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "query 必须为 1～500 字符的搜索关键词或表达式。");
        if (!Enum.TryParse<BeatmapsetSearchMode>(mode, true, out var searchMode) || !Enum.IsDefined(searchMode))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "mode 必须为 any、osu、taiko、catch 或 mania。");
        if (!Categories.TryGetValue(status, out var category))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "status 必须为 any、leaderboard、ranked、qualified、loved、pending、wip 或 graveyard。");
        BeatmapsetSearchSort? searchSort = null;
        if (sort is not null)
        {
            if (!Sorts.TryGetValue(sort, out var value)) return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "sort 不是有效的官方排序值。");
            searchSort = value;
        }
        if (cursor_string is not null && (string.IsNullOrWhiteSpace(cursor_string) || cursor_string.Length > 4096))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "cursor_string 必须为有效的分页游标，最多 4096 字符。");

        var key = ("beatmap-search", query, searchMode, category, searchSort, cursor_string);
        if (cache.TryGetValue<string>(key, out var cached)) return Content(cached!, "application/json");
        try
        {
            var result = await osuApi.SearchBeatmapsetsAsync(query, searchMode, category, searchSort, cursor_string, ct);
            if (!string.IsNullOrEmpty(result.Error))
                return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "osu! 搜索失败，请稍后重试。");
            // 保留客户端的 JsonProperty 字段名及枚举转换，避免 ASP.NET 默认序列化改变上游结构。
            var json = JsonConvert.SerializeObject(result, OsuClient.BuildJsonSettings());
            cache.Set(key, json, TimeSpan.FromSeconds(cachePolicy?.Value.SearchSeconds ?? 120));
            return Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to search osu! beatmapsets");
            return ApiErrors.Result(ErrorCatalog.OsuApiUnavailable, diagnostic: "osu! 搜索请求失败，请稍后重试。");
        }
    }
}
