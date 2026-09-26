using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HitCircleAPI.OpenApi;

/// <summary>一个成员的 XML 文档注释。</summary>
public sealed record XmlCommentMember(
    string? Summary,
    string? Remarks,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Responses);

/// <summary>
/// 读取编译器生成的 XML 文档注释，并按成员 ID（<c>M:Namespace.Type.Method(System.Int32)</c>）索引。
/// .NET 9 的 Microsoft.AspNetCore.OpenApi 不会自己读这个文件，转换器要靠它取描述文字。
/// </summary>
public sealed class XmlCommentsProvider
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, XmlCommentMember> _members;

    private XmlCommentsProvider(Dictionary<string, XmlCommentMember> members) => _members = members;

    private static XmlCommentsProvider Empty { get; } = new([]);

    /// <summary>拼出编译器写进 XML 的成员 ID。泛型参数要展开成 <c>Nullable{Int32}</c> 这种元数据写法。</summary>
    public static string MemberId(MethodInfo method)
    {
        var id = new StringBuilder("M:");
        id.Append(TypeName(method.DeclaringType!)).Append('.').Append(method.Name);

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            id.Append('(');
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0) id.Append(',');
                id.Append(ParameterTypeName(parameters[i].ParameterType));
            }
            id.Append(')');
        }

        return id.ToString();
    }

    /// <summary>加载 XML 文档注释文件。</summary>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public static XmlCommentsProvider FromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到 XML 文档注释文件", path);

        var members = new Dictionary<string, XmlCommentMember>(StringComparer.Ordinal);
        foreach (var element in XDocument.Load(path).Descendants("member"))
        {
            var name = element.Attribute("name")?.Value;
            if (!string.IsNullOrEmpty(name))
                members[name] = ReadMember(element);
        }

        return new XmlCommentsProvider(members);
    }

    /// <summary>加载程序集旁边的同名 XML 文档注释；没有该文件时返回空表（生产镜像不带它）。</summary>
    public static XmlCommentsProvider FromAssembly(Assembly assembly)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
        return File.Exists(path) ? FromFile(path) : Empty;
    }

    public bool TryGet(MethodInfo method, out XmlCommentMember? member) =>
        _members.TryGetValue(MemberId(method), out member);

    private static XmlCommentMember ReadMember(XElement element) => new(
        Summary: Text(element.Element("summary")),
        Remarks: Text(element.Element("remarks")),
        Parameters: Texts(element, "param", "name"),
        Responses: Texts(element, "response", "code"));

    private static Dictionary<string, string> Texts(XElement element, string tag, string attribute)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in element.Elements(tag))
        {
            var key = child.Attribute(attribute)?.Value;
            var text = Text(child);
            if (!string.IsNullOrEmpty(key) && text is not null)
                result[key] = text;
        }
        return result;
    }

    // 源码里的注释通常折成多行带缩进，直接塞进 OpenAPI 会把这些换行带进 UI。
    private static string? Text(XElement? element)
    {
        if (element is null) return null;
        var text = Whitespace.Replace(element.Value, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static string TypeName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string ParameterTypeName(Type type)
    {
        if (type.IsByRef)
            return ParameterTypeName(type.GetElementType()!) + "@";
        if (type.IsArray)
            return ParameterTypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return TypeName(type);

        var definition = type.GetGenericTypeDefinition().FullName!;
        var arity = definition.IndexOf('`');
        if (arity >= 0) definition = definition[..arity];

        var arguments = type.GetGenericArguments().Select(ParameterTypeName);
        return $"{definition}{{{string.Join(',', arguments)}}}";
    }
}
