using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HitCircleAPI.Controllers;

public record BindUserRequest(string OsuUsername, string Platform, string PlatformUid);
public record UnbindUserRequest(string Platform, string PlatformUid);
public record UpdateGameModeRequest(string Platform, string PlatformUid, int GameMode);

/// <summary>平台账号与 osu! 用户的绑定关系。</summary>
[ApiController]
[Route("users")]
public class UserController(
    AppDbContext db,
    IOsuApiService osuApi,
    ILogger<UserController> logger) : ControllerBase
{
    /// <summary>绑定平台账号与 osu! 用户。</summary>
    /// <param name="data">绑定信息。</param>
    /// <response code="200">绑定成功。</response>
    /// <response code="400">按用户名找不到 osu! 用户。</response>
    /// <response code="409">该平台账号已经绑定过。</response>
    /// <response code="500">写库失败。</response>
    [HttpPost("bind")]
    public async Task<IActionResult> BindUser([FromBody] BindUserRequest data)
    {
        Ossapi.Models.User osuUser;
        try
        {
            osuUser = await osuApi.GetUserAsync(data.OsuUsername);
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Failed to find osu user {Username}", data.OsuUsername);
            return BadRequest($"Failed to get osu user: {ex.Message}");
        }

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == data.Platform && u.PlatformUid == data.PlatformUid);
        if (existing is not null)
            return Conflict("User already bound");

        var user = new UserModel
        {
            OsuUid = osuUser.Id.ToString(),
            Platform = data.Platform,
            PlatformUid = data.PlatformUid,
            GameMode = 0
        };

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Database error binding user");
            return StatusCode(500, $"Database error: {ex.Message}");
        }

        return Ok(new { message = "bind user success" });
    }

    /// <summary>解除平台账号绑定。</summary>
    /// <param name="data">平台账号。</param>
    /// <response code="200">解绑成功。</response>
    /// <response code="404">该平台账号没有绑定记录。</response>
    /// <response code="500">写库失败。</response>
    [HttpPost("unbind")]
    public async Task<IActionResult> UnbindUser([FromBody] UnbindUserRequest data)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == data.Platform && u.PlatformUid == data.PlatformUid);
        if (user is null)
            return NotFound("User not found");

        db.Users.Remove(user);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Database error unbinding user");
            return StatusCode(500, $"Database error: {ex.Message}");
        }

        return Ok(new { message = "unbind user success" });
    }

    /// <summary>修改默认游戏模式。</summary>
    /// <param name="data">平台账号和模式。</param>
    /// <response code="200">修改成功。</response>
    /// <response code="404">该平台账号没有绑定记录。</response>
    /// <response code="500">写库失败。</response>
    [HttpPost("update_mode")]
    public async Task<IActionResult> UpdateMode([FromBody] UpdateGameModeRequest data)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Platform == data.Platform && u.PlatformUid == data.PlatformUid);
        if (user is null)
            return NotFound("User not found");

        user.GameMode = data.GameMode;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is not RetryableException)
        {
            logger.LogError(ex, "Database error updating game mode");
            return StatusCode(500, $"Database error: {ex.Message}");
        }

        return Ok(new { message = "update user game mode success" });
    }
}
