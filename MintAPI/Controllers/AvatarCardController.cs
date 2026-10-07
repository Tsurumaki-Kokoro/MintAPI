using MintAPI.Errors;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MintAPI.Data;
using MintAPI.Rendering.AvatarCardTheme;
using MintAPI.Services;

namespace MintAPI.Controllers;

/// <summary>方形玩家名片。</summary>
[ApiController]
[Route("user_info/avatar_card")]
public sealed class AvatarCardController(AppDbContext db, IOsuApiService osuApi, AvatarCardTheme theme,
    ILogger<AvatarCardController> logger) : ControllerBase
{
    /// <summary>生成淡紫底色的 512×512 PNG 名片，显示玩家头像、国旗和用户名。</summary>
    /// <param name="user">直接指定 osu! 数字 UID 或用户名，优先于平台绑定身份。</param>
    /// <param name="platform">未提供 user 时使用的绑定平台，例如 qq。</param>
    /// <param name="platform_uid">未提供 user 时使用的平台用户 ID。</param>
    /// <response code="200">512×512 PNG 图片。</response>
    /// <response code="400">未提供玩家或完整绑定身份。</response>
    /// <response code="404">绑定或 osu! 玩家不存在。</response>
    /// <response code="500">获取头像、外部调用或渲染失败。</response>
    /// <response code="503">API 配额或渲染资源暂时不足。</response>
    [HttpGet]
    [Produces("image/png", "application/problem+json")]
    public async Task<IActionResult> GetAvatarCard([FromQuery] string? user = null,
        [FromQuery] string? platform = null, [FromQuery] string? platform_uid = null)
    {
        var ct = HttpContext.RequestAborted;
        var target = user?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(platform_uid))
                return ApiErrors.Result(ErrorCatalog.InvalidArgument, message: "Provide user or both platform and platform_uid");
            var binding = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                u => u.Platform == platform && u.PlatformUid == platform_uid, ct);
            if (binding == null) return ApiErrors.Result(ErrorCatalog.UserNotBound, diagnostic: "User binding not found");
            target = binding.OsuUid;
        }
        try
        {
            var player = await osuApi.GetUserAsync(target).WaitAsync(ct);
            if (player.Id <= 0) return ApiErrors.Result(ErrorCatalog.InternalError, diagnostic: "Invalid player response");
            return File(await theme.RenderAsync(player, ct), "image/png");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ApiErrors.Result(ErrorCatalog.OsuUserNotFound, diagnostic: "osu! user not found");
        }
        catch (Exception ex) when (ex is not RetryableException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to generate avatar card for {User}", target);
            return ApiErrors.Result(ErrorCatalog.RenderFailed, diagnostic: "Failed to generate avatar card");
        }
    }
}
