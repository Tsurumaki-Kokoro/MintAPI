using HitCircleAPI.OpenApi;
using Microsoft.OpenApi.Models;

namespace HitCircleAPI.Tests.Unit;

public class XmlCommentsOperationTransformerTests
{
    private static XmlCommentMember Member(
        string? summary = null,
        string? remarks = null,
        Dictionary<string, string>? parameters = null,
        Dictionary<string, string>? responses = null) =>
        new(summary, remarks, parameters ?? [], responses ?? []);

    private static OpenApiOperation Operation() => new()
    {
        Parameters =
        [
            new OpenApiParameter { Name = "beatmap_id", In = ParameterLocation.Query },
            new OpenApiParameter { Name = "theme", In = ParameterLocation.Query },
        ],
        Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse { Description = "Success" },
            ["404"] = new OpenApiResponse(),
        },
    };

    [Fact]
    public void Apply_sets_the_summary_from_the_summary_tag()
    {
        var operation = Operation();

        XmlCommentsOperationTransformer.Apply(
            operation, Member(summary: "查询谱面信息。"));

        Assert.Equal("查询谱面信息。", operation.Summary);
    }

    [Fact]
    public void Apply_sets_the_description_from_the_remarks_tag()
    {
        var operation = Operation();

        XmlCommentsOperationTransformer.Apply(
            operation, Member(summary: "一句话。", remarks: "补充说明。"));

        Assert.Equal("一句话。", operation.Summary);
        Assert.Equal("补充说明。", operation.Description);
    }

    [Fact]
    public void Apply_describes_the_parameters_it_finds_by_name()
    {
        var operation = Operation();

        XmlCommentsOperationTransformer.Apply(
            operation,
            Member(parameters: new Dictionary<string, string>
            {
                ["beatmap_id"] = "谱面 ID。",
                ["theme"] = "主题名。",
            }));

        Assert.Equal("谱面 ID。", operation.Parameters[0].Description);
        Assert.Equal("主题名。", operation.Parameters[1].Description);
    }

    // 查询参数、路径参数之外的参数（[FromBody]）在 OpenAPI 里是 requestBody，
    // 不在 Parameters 里，必须单独认。
    [Fact]
    public void Apply_describes_the_request_body_from_its_parameter()
    {
        var operation = Operation();
        operation.RequestBody = new OpenApiRequestBody();

        XmlCommentsOperationTransformer.Apply(
            operation,
            Member(parameters: new Dictionary<string, string> { ["data"] = "绑定信息。" }),
            bodyParameterName: "data");

        Assert.Equal("绑定信息。", operation.RequestBody.Description);
    }

    [Fact]
    public void Apply_describes_the_responses_it_finds_by_status_code()
    {
        var operation = Operation();

        XmlCommentsOperationTransformer.Apply(
            operation,
            Member(responses: new Dictionary<string, string>
            {
                ["200"] = "PNG 图片。",
                ["404"] = "用户不存在。",
            }));

        Assert.Equal("PNG 图片。", operation.Responses["200"].Description);
        Assert.Equal("用户不存在。", operation.Responses["404"].Description);
    }

    // 注释里写了、文档里却没有的状态码（例如实现里没返回过）不该凭空造一个出来。
    [Fact]
    public void Apply_ignores_documented_names_the_operation_does_not_carry()
    {
        var operation = Operation();
        operation.RequestBody = new OpenApiRequestBody();

        XmlCommentsOperationTransformer.Apply(
            operation,
            Member(parameters: new Dictionary<string, string> { ["nonsense"] = "无关。" },
                   responses: new Dictionary<string, string> { ["503"] = "不存在。" }),
            bodyParameterName: "data");

        Assert.Equal(2, operation.Parameters.Count);
        Assert.Null(operation.RequestBody.Description);
        Assert.False(operation.Responses.ContainsKey("503"));
    }

    [Fact]
    public void Apply_leaves_the_operation_untouched_when_the_member_is_empty()
    {
        var operation = Operation();

        XmlCommentsOperationTransformer.Apply(operation, Member());

        Assert.Null(operation.Summary);
        Assert.Null(operation.Description);
        Assert.Null(operation.Parameters[0].Description);
        Assert.Equal("Success", operation.Responses["200"].Description);
    }
}
