using MintAPI.Errors;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintOsuApi.Enums;

namespace MintAPI.Controllers;

public sealed record UserRankingRequest(
    [property: Required] string Platform,
    [property: Required, MinLength(1), MaxLength(100)] string[] PlatformUids);
public sealed record UserRankingEntry(string PlatformUid, string OsuUid, string Username, int? Rank, double? Pp, double? Acc, string AvatarUrl = "", string CountryCode = "");
public sealed record UserModeRanking(int GameMode, IReadOnlyList<UserRankingEntry> Users);

/// <summary>指定绑定用户列表的全球排名。</summary>
[ApiController]
[Route("users/ranking")]
public sealed class UserRankingController(AppDbContext db, IOsuApiService osuApi, UserRankingRenderer renderer) : ControllerBase
{
    /// <summary>返回指定模式的 rank、PP 和百分制 acc，全球排名数字升序，无排名者置后。</summary>
    /// <param name="data">绑定平台及平台用户 ID 列表，最多 100 个，重复 ID 只返回一次。</param>
    /// <param name="game_mode">模式：0 osu!、1 taiko、2 catch、3 mania。</param>
    /// <param name="format">png（默认）返回排行榜图片，json 返回数据。</param>
    /// <response code="200">排行榜 PNG 或排序后的用户列表。</response>
    /// <response code="400">用户列表或模式无效。</response>
    /// <response code="404">列表中存在未绑定用户，返回其平台 ID。</response>
    [HttpPost]
    [Produces("image/png", "application/json", "application/problem+json")]
    public Task<IActionResult> Ranking([FromBody] UserRankingRequest data, [FromQuery, Required] int? game_mode, [FromQuery] string format = "png")
        => QueryAsync(data, game_mode, false, format);

    /// <summary>返回同一绑定用户列表在四个模式各自的前五名；人数不足五人时返回全部。</summary>
    /// <param name="data">绑定平台及平台用户 ID 列表，最多 100 个，重复 ID 只返回一次。</param>
    /// <param name="format">png（默认）返回四模式合图，json 返回数据。</param>
    /// <response code="200">四模式 PNG 或模式 0–3 各自的前五名，rank 数字升序，无排名者置后。</response>
    /// <response code="400">用户列表无效。</response>
    /// <response code="404">列表中存在未绑定用户，返回其平台 ID。</response>
    [HttpPost("top5")]
    [Produces("image/png", "application/json", "application/problem+json")]
    public Task<IActionResult> TopFive([FromBody] UserRankingRequest data, [FromQuery] string format = "png") => QueryAsync(data, null, true, format);

    private async Task<IActionResult> QueryAsync(UserRankingRequest data, int? mode, bool topFive, string format)
    {
        if (format is not ("png" or "json") || (!topFive && mode == null) || mode is < 0 or > 3 || string.IsNullOrWhiteSpace(data.Platform)
            || data.PlatformUids is not { Length: > 0 and <= 100 }
            || data.PlatformUids.Any(string.IsNullOrWhiteSpace))
            return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Invalid platform, user list or game mode");

        var ct = HttpContext.RequestAborted;
        var ids = data.PlatformUids.Distinct(StringComparer.Ordinal).ToArray();
        var candidates = await db.Users.AsNoTracking()
            .Where(u => u.Platform == data.Platform && ids.Contains(u.PlatformUid)).ToListAsync(ct);
        // Use exact identity matching even when the database collation is case insensitive.
        var bindings = candidates.Where(u => u.Platform == data.Platform && ids.Contains(u.PlatformUid, StringComparer.Ordinal)).ToList();
        var missing = ids.Except(bindings.Select(u => u.PlatformUid), StringComparer.Ordinal).ToArray();
        if (missing.Length > 0) return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "Users not bound: " + string.Join(",", missing));

        var result = new List<UserModeRanking>();
        foreach (var gameMode in topFive ? new[] { 0, 1, 2, 3 } : new[] { mode!.Value })
        {
            var entries = new List<UserRankingEntry>();
            foreach (var group in bindings.GroupBy(u => u.OsuUid))
            {
                var user = await osuApi.GetUserAsync(group.Key, (GameMode)gameMode).WaitAsync(ct);
                var stats = user.Statistics;
                entries.AddRange(group.Select(binding => new UserRankingEntry(binding.PlatformUid, binding.OsuUid,
                    user.Username, stats?.GlobalRank is > 0 ? stats.GlobalRank : null, stats?.Pp, stats?.HitAccuracy, user.AvatarUrl, user.CountryCode)));
            }
            var ordered = entries.OrderBy(e => e.Rank ?? int.MaxValue)
                .ThenBy(e => e.OsuUid, StringComparer.Ordinal).ThenBy(e => e.PlatformUid, StringComparer.Ordinal);
            result.Add(new UserModeRanking(gameMode, (topFive ? ordered.Take(5) : ordered).ToList()));
        }
        if (format == "png") return File(await renderer.RenderAsync(data.Platform, result, topFive, ct), "image/png");
        return topFive ? Ok(result) : Ok(result[0]);
    }
}
