using System.Reflection;
using HitCircleAPI.OpenApi;

namespace HitCircleAPI.Tests.Unit;

public class XmlCommentsProviderTests
{
    // 被测的成员 ID 由 C# 编译器写进 XML 文档：这里用字面量钉死格式，
    // 因为它是本项目与那个格式之间的唯一契约。
    private const string NoArgsId = "M:HitCircleAPI.Tests.Unit.XmlCommentsProviderTests.Sample.NoArgs";
    private const string NullableId = "M:HitCircleAPI.Tests.Unit.XmlCommentsProviderTests.Sample.Nullable(System.Nullable{System.Int32})";
    private const string MixedId = "M:HitCircleAPI.Tests.Unit.XmlCommentsProviderTests.Sample.Mixed(System.Nullable{System.Int32},System.String,System.Boolean)";
    private const string ArrayId = "M:HitCircleAPI.Tests.Unit.XmlCommentsProviderTests.Sample.ArrayParam(System.Int32[])";

    private static class Sample
    {
        public static void NoArgs() { }
        public static void Nullable(int? beatmap_id) { }
        public static void Mixed(int? beatmap_id, string theme, bool include_fails) { }
        public static void ArrayParam(int[] ids) { }
    }

    private static MethodInfo Method(string name) =>
        typeof(Sample).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;

    private static XmlCommentMember Documented(XmlCommentsProvider provider, string methodName)
    {
        Assert.True(provider.TryGet(Method(methodName), out var member));
        return member!;
    }

    [Fact]
    public void MemberId_omits_the_parentheses_for_a_method_without_parameters()
    {
        Assert.Equal(NoArgsId, XmlCommentsProvider.MemberId(Method(nameof(Sample.NoArgs))));
    }

    // 可空值是本项目最常见的参数形态（全部 [FromQuery] int?），
    // 而它的 XML 形态 Nullable{Int32} 与 C# 写法毫无相似之处，最容易写错。
    [Fact]
    public void MemberId_expands_a_nullable_parameter_to_its_metadata_name()
    {
        Assert.Equal(NullableId, XmlCommentsProvider.MemberId(Method(nameof(Sample.Nullable))));
    }

    [Fact]
    public void MemberId_formats_every_parameter_in_order()
    {
        Assert.Equal(MixedId, XmlCommentsProvider.MemberId(Method(nameof(Sample.Mixed))));
    }

    [Fact]
    public void MemberId_appends_brackets_for_an_array_parameter()
    {
        Assert.Equal(ArrayId, XmlCommentsProvider.MemberId(Method(nameof(Sample.ArrayParam))));
    }

    [Fact]
    public void FromFile_reads_the_summary_of_a_member()
    {
        using var doc = new TempDoc($"""
            <member name="{NullableId}">
              <summary>渲染谱面图片。</summary>
            </member>
            """);

        var provider = XmlCommentsProvider.FromFile(doc.Path);

        Assert.Equal("渲染谱面图片。", Documented(provider, nameof(Sample.Nullable)).Summary);
    }

    // 源码里的 summary 通常折成多行并带缩进，直接塞进 OpenAPI 会把换行带进 UI。
    [Fact]
    public void FromFile_collapses_a_multi_line_summary_into_one_line()
    {
        using var doc = new TempDoc($"""
            <member name="{NullableId}">
              <summary>
                渲染谱面图片。
                第二行。
              </summary>
            </member>
            """);

        var provider = XmlCommentsProvider.FromFile(doc.Path);

        Assert.Equal("渲染谱面图片。 第二行。", Documented(provider, nameof(Sample.Nullable)).Summary);
    }

    [Fact]
    public void FromFile_reads_param_and_response_descriptions()
    {
        using var doc = new TempDoc($"""
            <member name="{MixedId}">
              <summary>混合参数。</summary>
              <param name="beatmap_id">谱面 ID。</param>
              <param name="theme">主题名。</param>
              <response code="200">PNG 图片。</response>
              <response code="404">用户不存在。</response>
            </member>
            """);

        var member = Documented(XmlCommentsProvider.FromFile(doc.Path), nameof(Sample.Mixed));

        Assert.Equal("谱面 ID。", member.Parameters["beatmap_id"]);
        Assert.Equal("主题名。", member.Parameters["theme"]);
        Assert.Equal("PNG 图片。", member.Responses["200"]);
        Assert.Equal("用户不存在。", member.Responses["404"]);
    }

    [Fact]
    public void FromFile_reads_remarks_as_the_description()
    {
        using var doc = new TempDoc($"""
            <member name="{NoArgsId}">
              <summary>一句话。</summary>
              <remarks>补充说明。</remarks>
            </member>
            """);

        var provider = XmlCommentsProvider.FromFile(doc.Path);

        Assert.Equal("补充说明。", Documented(provider, nameof(Sample.NoArgs)).Remarks);
    }

    [Fact]
    public void TryGet_returns_false_for_a_member_that_has_no_documentation()
    {
        using var doc = new TempDoc($"""
            <member name="{NoArgsId}">
              <summary>有注释。</summary>
            </member>
            """);

        var provider = XmlCommentsProvider.FromFile(doc.Path);

        Assert.False(provider.TryGet(Method(nameof(Sample.Nullable)), out _));
    }

    [Fact]
    public void FromFile_throws_when_the_file_does_not_exist()
    {
        Assert.Throws<FileNotFoundException>(
            () => XmlCommentsProvider.FromFile(Path.Combine(Path.GetTempPath(), "definitely-missing.xml")));
    }

    // 生产镜像里不带 XML 文档，缺失必须是"没有描述"而不是启动失败。
    [Fact]
    public void FromAssembly_returns_an_empty_provider_when_no_xml_sits_next_to_it()
    {
        var provider = XmlCommentsProvider.FromAssembly(typeof(XmlCommentsProviderTests).Assembly);

        Assert.False(provider.TryGet(Method(nameof(Sample.NoArgs)), out _));
    }

    private sealed class TempDoc : IDisposable
    {
        public string Path { get; }

        public TempDoc(string members)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xmlcomments-{Guid.NewGuid():N}.xml");
            File.WriteAllText(Path, $"""
                <?xml version="1.0"?>
                <doc>
                  <members>
                    {members}
                  </members>
                </doc>
                """);
        }

        public void Dispose() => File.Delete(Path);
    }
}
