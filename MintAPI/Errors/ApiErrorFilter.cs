using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MintAPI.Services;

namespace MintAPI.Errors;

/// <summary>在格式协商前固定错误 JSON，保留原始错误信息到日志。</summary>
public sealed class ApiErrorFilter(ILogger<ApiErrorFilter> logger) : IAsyncAlwaysRunResultFilter, IAsyncExceptionFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var status = context.Result switch
        {
            ObjectResult result => result.StatusCode,
            StatusCodeResult result => result.StatusCode,
            JsonResult result => result.StatusCode,
            _ => null
        };
        if (status is >= 400)
        {
            var value = context.Result switch
            {
                ObjectResult objectResult => objectResult.Value,
                JsonResult jsonResult => jsonResult.Value,
                _ => null
            };
            var original = value is string text ? text : value?.ToString();
            var error = value is ApiError explicitError
                ? ApiErrors.Create(context.HttpContext, new ErrorDefinition(explicitError.Status, explicitError.Code, explicitError.Message))
                : Classify(context.HttpContext, status.Value, value is ProblemDetails ? null : original);
            if (!context.HttpContext.Items.ContainsKey(typeof(ApiErrorFilter)))
                Log(context.HttpContext, error, value is ApiError typed ? typed.Diagnostic ?? typed.Message : original);
            context.Result = new JsonResult(error) { StatusCode = error.Status, ContentType = "application/problem+json" };
        }
        await next();
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is RetryableException || context.HttpContext.RequestAborted.IsCancellationRequested)
            return Task.CompletedTask;
        var definition = context.Exception switch
        {
            ApiLookupException lookup => lookup.Definition,
            HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => ErrorCatalog.RecordNotFound,
            HttpRequestException or MintOsuApi.OsuApiException => ErrorCatalog.OsuApiUnavailable,
            _ => ErrorCatalog.InternalError
        };
        var error = ApiErrors.Create(context.HttpContext, definition);
        logger.Log(error.Status >= 500 ? LogLevel.Error : LogLevel.Information, context.Exception,
            "API exception {Code} {Status} {TraceId} {Method} {Path} {RequestContext}",
            error.Code, error.Status, error.TraceId, context.HttpContext.Request.Method, context.HttpContext.Request.Path, ApiErrors.Context(context.HttpContext));
        context.Result = new JsonResult(error) { StatusCode = error.Status, ContentType = "application/problem+json" };
        context.HttpContext.Items[typeof(ApiErrorFilter)] = true;
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }

    private void Log(HttpContext context, ApiError error, string? original)
        => logger.Log(error.Status >= 500 ? LogLevel.Error : LogLevel.Information,
            "API error {Code} {Status} {TraceId} {Method} {Path} {RequestContext} Reason: {Reason}",
            error.Code, error.Status, error.TraceId, context.Request.Method, context.Request.Path, ApiErrors.Context(context), original);

    // 仅兼容旧调用方；控制器新分支必须显式使用 ErrorCatalog。
    private static ApiError Classify(HttpContext context, int status, string? reason)
    {
        var (code, message) = reason switch
        {
            "User not found" or "User binding not found" or "Users not bound" => ("USER_NOT_BOUND", "请先绑定 osu! 账号"),
            "osu! user not found" => ("OSU_USER_NOT_FOUND", "未找到该 osu! 用户"),
            "No recent play record found" => ("RECENT_PLAY_NOT_FOUND", "未查询到符合条件的最近游玩记录"),
            "No best play record found" or "No play record found" => ("BEST_PLAY_NOT_FOUND", "未查询到该模式下的 BP 成绩"),
            "No score found for this beatmap" => ("SCORE_NOT_FOUND", "该模式下未查询到你在此谱面的成绩"),
            "No history found" => ("HISTORY_NOT_FOUND", "暂未查询到该玩家的历史数据"),
            "No scores found for this page" => ("SCORE_PAGE_NOT_FOUND", "该页没有符合条件的成绩"),
            _ when status == 400 => ("INVALID_ARGUMENT", reason ?? "请求参数有误"),
            _ when status == 404 => ("RECORD_NOT_FOUND", reason ?? "未查询到相关记录"),
            _ when status == 401 => ("UNAUTHORIZED", "请提供有效的身份凭据"),
            _ when status == 403 => ("FORBIDDEN", "没有访问该资源的权限"),
            _ when status == 410 => ("RESOURCE_EXPIRED", "该资源已过期，请重新创建"),
            _ when status == 409 => ("CONFLICT", reason ?? "当前状态与请求冲突"),
            _ when status == 502 => ("OSU_API_UNAVAILABLE", "osu! 服务暂时不可用，请稍后重试"),
            _ when status == 503 => ("SERVICE_BUSY", "服务繁忙，请稍后重试"),
            _ when reason?.Contains("render", StringComparison.OrdinalIgnoreCase) == true || reason?.Contains("图片失败") == true => ("RENDER_FAILED", "图片生成失败，请稍后重试"),
            _ => ("INTERNAL_ERROR", "服务发生异常，请联系维护者")
        };
        if (reason?.StartsWith("Failed to get user info") == true || reason?.StartsWith("Failed to get scores") == true
            || reason?.StartsWith("Failed to get user score") == true || reason?.StartsWith("Failed to get beatmap:") == true
            || reason?.StartsWith("Failed to get beatmapset:") == true)
        {
            status = 502;
            code = "OSU_API_UNAVAILABLE";
            message = "osu! 服务暂时不可用，请稍后重试";
        }
        return ApiErrors.Create(context, new ErrorDefinition(status, code, message));
    }
}
