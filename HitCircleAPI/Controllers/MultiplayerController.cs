using Microsoft.AspNetCore.Mvc;

namespace HitCircleAPI.Controllers;

/// <summary>多人对局历史与评分（均未实现）。</summary>
[ApiController]
[Route("multiplayer")]
public class MultiplayerController : ControllerBase
{
    /// <summary>多人对局历史图（尚未实现）。</summary>
    /// <param name="mp_id">对局 ID。</param>
    /// <param name="theme">渲染主题，默认 default。</param>
    /// <response code="501">尚未实现。</response>
    [HttpGet("history")]
    public IActionResult GetMatchHistory([FromQuery] int? mp_id = null, [FromQuery] string theme = "default")
    {
        return StatusCode(501, "Multiplayer history not yet implemented");
    }

    /// <summary>多人对局评分图（尚未实现）。</summary>
    /// <param name="mp_id">对局 ID。</param>
    /// <param name="algorithm">评分算法，默认 osuplus。</param>
    /// <param name="theme">渲染主题，默认 default。</param>
    /// <response code="501">尚未实现。</response>
    [HttpGet("rating")]
    public IActionResult GetRating([FromQuery] int? mp_id = null, [FromQuery] string algorithm = "osuplus", [FromQuery] string theme = "default")
    {
        return StatusCode(501, "Multiplayer rating not yet implemented");
    }
}
