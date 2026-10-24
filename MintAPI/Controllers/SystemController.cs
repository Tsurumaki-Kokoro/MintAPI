using Microsoft.AspNetCore.Mvc;
using MintAPI.Errors;
using MintAPI.Services;

namespace MintAPI.Controllers;

/// <summary>服务身份、插件能力发现与部署健康检查。</summary>
[ApiController]
public sealed class SystemController(ServiceInformation information) : ControllerBase
{
    /// <summary>获取版本、构建信息、已启用能力和运行组件版本；沿用 access_token 鉴权。</summary>
    /// <response code="200">不包含凭据、内部路径或连接地址的服务信息。</response>
    [HttpGet("api/info")]
    [Produces("application/json", "application/problem+json")]
    public IActionResult Info() => Ok(information.Get());

    /// <summary>仅确认服务进程能响应，无需鉴权。</summary>
    /// <response code="200">进程存活。</response>
    [HttpGet("health/live")]
    [Produces("application/json")]
    public IActionResult Live() => Ok(new { status = "alive" });

    /// <summary>检查数据库、缓存、浏览器及预览工具是否就绪，无需鉴权且不返回依赖细节。</summary>
    /// <response code="200">服务就绪。</response>
    /// <response code="503">依赖未就绪；application/problem+json，附 Retry-After。</response>
    [HttpGet("health/ready")]
    [Produces("application/json", "application/problem+json")]
    [ProducesResponseType(typeof(ApiError), 503, "application/problem+json")]
    public async Task<IActionResult> Ready([FromServices] IReadinessProbe readiness)
    {
        if (await readiness.IsReadyAsync(HttpContext.RequestAborted)) return Ok(new { status = "ready" });
        Response.Headers.RetryAfter = "10";
        return ApiErrors.Result(ErrorCatalog.ServiceNotReady);
    }
}
