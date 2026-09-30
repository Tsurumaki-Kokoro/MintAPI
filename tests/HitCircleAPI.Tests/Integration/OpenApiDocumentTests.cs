using System.Text.Json;
using HitCircleAPI.Controllers;
using HitCircleAPI.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HitCircleAPI.Tests.Integration;

/// <summary>
/// 起一个只挂控制器和 OpenAPI 的最小宿主机，取回真实生成的文档。
/// 它同时证明三件事：XML 文件被复制到了输出目录、成员 ID 对得上、转换器挂进了管道。
///
/// 断言只钉住"哪条注释落到了哪个位置"，不钉住文案本身 ——
/// 改注释措辞是编辑文档，不该让测试变红。
/// </summary>
public class OpenApiDocumentTests
{
    private static async Task<JsonDocument> GetDocumentAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(BeatmapController).Assembly);
        builder.Services.AddOpenApi(options => options.AddXmlComments(typeof(BeatmapController).Assembly));

        var app = builder.Build();
        app.MapControllers();
        app.MapOpenApi();

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            // 端口写 0 由内核分配，启动后回读真实地址。
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!;

            using var http = new HttpClient();
            var json = await http.GetStringAsync($"{addresses.Addresses.First()}/openapi/v1.json");
            return JsonDocument.Parse(json);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<JsonElement> GetOperationAsync(string path, string method)
    {
        using var document = await GetDocumentAsync();
        return document.RootElement
            .GetProperty("paths").GetProperty(path).GetProperty(method)
            .Clone();
    }

    // 新增端点忘了写注释，这条会先红：文档的价值全在有描述。
    [Fact]
    public async Task Every_operation_carries_a_summary()
    {
        using var document = await GetDocumentAsync();

        var undocumented = (
            from path in document.RootElement.GetProperty("paths").EnumerateObject()
            from operation in path.Value.EnumerateObject()
            where !operation.Value.TryGetProperty("summary", out var summary)
                  || string.IsNullOrWhiteSpace(summary.GetString())
            select $"{operation.Name.ToUpperInvariant()} {path.Name}").ToList();

        Assert.Empty(undocumented);
    }

    [Fact]
    public async Task Operation_carries_the_summary_from_the_xml_comment()
    {
        var operation = await GetOperationAsync("/beatmap/info", "get");

        Assert.StartsWith("渲染谱面或谱面集信息图", operation.GetProperty("summary").GetString());
    }

    // 每个参数要拿到自己那条注释，而不是全部为空或全部一样。
    [Fact]
    public async Task Each_query_parameter_carries_its_own_description()
    {
        var operation = await GetOperationAsync("/beatmap/info", "get");

        var descriptions = operation.GetProperty("parameters").EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("name").GetString()!,
                p => p.GetProperty("description").GetString());

        Assert.StartsWith("谱面 ID", descriptions["beatmap_id"]);
        Assert.StartsWith("渲染主题", descriptions["theme"]);
    }

    [Fact]
    public async Task Response_carries_its_description()
    {
        var operation = await GetOperationAsync("/beatmap/info", "get");

        var ok = operation.GetProperty("responses").GetProperty("200").GetProperty("description").GetString();

        Assert.Contains("PNG", ok);
    }

    // 图片接口返回 File(...)，生成器猜不出内容类型，得靠 [Produces] 声明。
    [Fact]
    public async Task Image_response_declares_its_content_type()
    {
        var operation = await GetOperationAsync("/beatmap/info", "get");

        var content = operation.GetProperty("responses").GetProperty("200").GetProperty("content");

        Assert.True(content.TryGetProperty("image/png", out _));
    }

    [Fact]
    public async Task Performance_routes_keep_their_separate_response_types()
    {
        var control = await GetOperationAsync("/user_info/extra/performance_control", "get");
        var analyze = await GetOperationAsync("/user_info/extra/performance_analyze", "get");

        var controlResponse = control.GetProperty("responses").GetProperty("200");
        var analyzeContent = analyze.GetProperty("responses").GetProperty("200").GetProperty("content");
        Assert.Contains("JSON", controlResponse.GetProperty("description").GetString());
        Assert.False(controlResponse.TryGetProperty("content", out var controlContent)
            && controlContent.TryGetProperty("image/png", out _));
        Assert.True(analyzeContent.TryGetProperty("image/png", out _));
    }

    // 请求体参数的注释要落在 requestBody 上，而不是被当成查询参数丢掉。
    [Fact]
    public async Task Request_body_carries_the_description_of_its_parameter()
    {
        var operation = await GetOperationAsync("/users/bind", "post");

        var description = operation.GetProperty("requestBody").GetProperty("description").GetString();

        Assert.StartsWith("绑定信息", description);
    }

    // 源码里的注释折成多行带缩进，换行漏进 JSON 会在 UI 里变成断行。
    [Fact]
    public async Task Descriptions_carry_no_line_breaks_from_the_source()
    {
        var operation = await GetOperationAsync("/beatmap/info", "get");

        var texts = new List<string?> { operation.GetProperty("summary").GetString() };
        texts.AddRange(operation.GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("description").GetString()));
        texts.Add(operation.GetProperty("responses").GetProperty("200").GetProperty("description").GetString());

        Assert.All(texts, text =>
        {
            Assert.NotNull(text);
            Assert.DoesNotContain('\n', text);
            Assert.Equal(text.Trim(), text);
        });
    }
}
