using Microsoft.AspNetCore.Mvc;

namespace HitCircleAPI.Controllers;

[ApiController]
[Route("multiplayer")]
public class MultiplayerController : ControllerBase
{
    [HttpGet("history")]
    public IActionResult GetMatchHistory([FromQuery] int? mp_id = null, [FromQuery] string theme = "default")
    {
        return StatusCode(501, "Multiplayer history not yet implemented");
    }

    [HttpGet("rating")]
    public IActionResult GetRating([FromQuery] int? mp_id = null, [FromQuery] string algorithm = "osuplus", [FromQuery] string theme = "default")
    {
        return StatusCode(501, "Multiplayer rating not yet implemented");
    }
}
