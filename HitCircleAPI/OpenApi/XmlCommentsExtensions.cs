using System.Reflection;
using Microsoft.AspNetCore.OpenApi;

namespace HitCircleAPI.OpenApi;

public static class XmlCommentsExtensions
{
    /// <summary>
    /// 让 OpenAPI 文档用上程序集的 XML 文档注释。
    /// .NET 9 的 Microsoft.AspNetCore.OpenApi 不带这个能力，需要自己接。
    /// 找不到 XML 文件时静默降级：文档照常生成，只是没有描述文字。
    /// </summary>
    public static OpenApiOptions AddXmlComments(this OpenApiOptions options, Assembly assembly) =>
        options.AddOperationTransformer(
            new XmlCommentsOperationTransformer(XmlCommentsProvider.FromAssembly(assembly)));
}
