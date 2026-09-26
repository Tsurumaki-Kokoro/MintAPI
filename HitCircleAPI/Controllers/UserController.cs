using HitCircleAPI.Data;
using HitCircleAPI.Models.Entities;
using HitCircleAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HitCircleAPI.Controllers;

public record BindUserRequest(string OsuUsername, string Platform, string PlatformUid);
public record UnbindUserRequest(string Platform, string PlatformUid);
public record UpdateGameModeRequest(string Platform, string PlatformUid, int GameMode);

[ApiController]
[Route("users")]
public class UserController(
    AppDbContext db,
    IOsuApiService osuApi,
    ILogger<UserController> logger) : ControllerBase
{
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
