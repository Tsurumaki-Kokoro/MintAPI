using System.Reflection;
using Microsoft.AspNetCore.OpenApi;

namespace HitCircleAPI.OpenApi;

public static class XmlCommentsExtensions
{
    /// <summary>将程序集 XML 注释添加到 OpenAPI 文档。</summary>
    public static OpenApiOptions AddXmlComments(this OpenApiOptions options, Assembly assembly) =>
        options.AddOperationTransformer(
            new XmlCommentsOperationTransformer(XmlCommentsProvider.FromAssembly(assembly)));
}
