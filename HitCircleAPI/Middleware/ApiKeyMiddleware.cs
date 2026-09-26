namespace HitCircleAPI.Middleware;

public class ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private const string ApiKeyHeaderName = "access_token";

    // 空值必须在构造期炸掉，不能拖到请求期：若 _apiKey 是 ""，
    // 客户端发一个空值头 `access_token: ` 就能通过下面的比较，整个 API 裸奔。
    // 本中间件由 UseMiddleware 在构建管道时实例化一次，所以在这里抛 = 启动失败。
    private readonly string _apiKey = RequireApiKey(configuration);

    private static string RequireApiKey(IConfiguration configuration)
    {
        var apiKey = configuration["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "配置项 ApiKey 未设置或为空，拒绝启动。本地请写入 " +
                "HitCircleAPI/appsettings.Development.json（已 gitignore，可参考同目录 .example），" +
                "生产请通过环境变量 ApiKey 注入。");
        }

        return apiKey;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // OpenAPI / health 路径跳过鉴权
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/openapi") || path.StartsWith("/scalar") || path.StartsWith("/health"))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Access Token required");
            return;
        }

        if (!string.Equals(extractedApiKey, _apiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Could not validate credentials");
            return;
        }

        await next(context);
    }
}
