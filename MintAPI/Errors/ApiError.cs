using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MintAPI.Errors;

/// <summary>机器人可按 Code 转换提示；TraceId 用于关联服务端日志。</summary>
public sealed record ApiError(int Status, string Code, string Message, string TraceId)
{
    [JsonIgnore]
    public string? Diagnostic { get; init; }
    public override string ToString() => $"{Status} {Code}: {Message}";
}

public static class ApiErrors
{
    public static ObjectResult Result(ErrorDefinition definition, string? message = null, string? diagnostic = null)
    {
        var error = new ApiError(definition.Status, definition.Code, definition.Status >= 500 ? definition.Message : message ?? definition.Message, "") { Diagnostic = diagnostic };
        return definition.Status switch
        {
            400 => new BadRequestObjectResult(error),
            404 => new NotFoundObjectResult(error),
            409 => new ConflictObjectResult(error),
            _ => new ObjectResult(error) { StatusCode = definition.Status }
        };
    }

    public static ApiError Create(HttpContext context, ErrorDefinition definition, string? message = null)
        => new(definition.Status, definition.Code, definition.Status >= 500 ? definition.Message : message ?? definition.Message, Activity.Current?.Id ?? context.TraceIdentifier);

    public static string Context(HttpContext context)
    {
        string[] keys = ["platform", "platform_uid", "beatmap_id", "beatmapset_id", "game_mode", "mp_id"];
        return string.Join(" ", keys.Where(context.Request.Query.ContainsKey)
            .Select(key => $"{key}={context.Request.Query[key].ToString()[..Math.Min(context.Request.Query[key].ToString().Length, 256)]}"));
    }
}

public sealed class ApiLookupException(ErrorDefinition definition, Exception inner) : Exception(definition.Message, inner)
{
    public ErrorDefinition Definition { get; } = definition;
}
