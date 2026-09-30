using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace HitCircleAPI.OpenApi;

/// <summary>将 XML 文档注释写入 OpenAPI 操作。</summary>
public sealed class XmlCommentsOperationTransformer(XmlCommentsProvider comments) : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor action)
            return Task.CompletedTask;

        if (!comments.TryGet(action.MethodInfo, out var member) || member is null)
            return Task.CompletedTask;

        // 请求体参数单独处理。
        var bodyParameterName = context.Description.ParameterDescriptions
            .FirstOrDefault(p => p.Source == BindingSource.Body)?.Name;

        Apply(operation, member, bodyParameterName);
        return Task.CompletedTask;
    }

    /// <summary>将成员注释应用到 OpenAPI 操作。</summary>
    public static void Apply(OpenApiOperation operation, XmlCommentMember member, string? bodyParameterName = null)
    {
        if (member.Summary is not null)
            operation.Summary = member.Summary;
        if (member.Remarks is not null)
            operation.Description = member.Remarks;

        foreach (var (name, description) in member.Parameters)
        {
            if (name == bodyParameterName)
            {
                if (operation.RequestBody is not null)
                    operation.RequestBody.Description = description;
                continue;
            }

            var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == name);
            if (parameter is not null)
                parameter.Description = description;
        }

        foreach (var (statusCode, description) in member.Responses)
        {
            if (operation.Responses.TryGetValue(statusCode, out var response))
                response.Description = description;
        }
    }
}
