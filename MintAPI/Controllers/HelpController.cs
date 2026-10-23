using Microsoft.AspNetCore.Mvc;
using MintAPI.Errors;
using MintAPI.Rendering.HelpTheme;
using MintAPI.Services;

namespace MintAPI.Controllers;

/// <summary>机器人指令帮助海报。</summary>
[ApiController]
[Route("help")]
public sealed class HelpController(HelpTheme theme, ILogger<HelpController> logger) : ControllerBase
{
    /// <summary>生成 Mint osu! 横版指令帮助海报，无需绑定玩家账号。</summary>
    /// <response code="200">PNG 帮助海报。</response>
    /// <response code="500">图片生成失败；application/problem+json。</response>
    /// <response code="503">渲染服务繁忙，按 Retry-After 重试；application/problem+json。</response>
    [HttpGet("image")]
    [Produces("image/png", "application/problem+json")]
    [ProducesResponseType(typeof(byte[]), 200, "image/png")]
    [ProducesResponseType(typeof(ApiError), 500, "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 503, "application/problem+json")]
    public async Task<IActionResult> Image()
    {
        try
        {
            return File(await theme.RenderAsync(HttpContext.RequestAborted), "image/png");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render command help poster");
            return ApiErrors.Result(ErrorCatalog.RenderFailed);
        }
    }
}
