using HitCircleAPI.Data;
using HitCircleAPI.Middleware;
using HitCircleAPI.OpenApi;
using HitCircleAPI.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using System.Threading.RateLimiting;

// ── Serilog 配置 ────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build())
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine("logs", ".log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── 使用 Serilog 替换默认日志 ────────────────────────────────
    builder.Host.UseSerilog();

    // ── 配置绑定 ─────────────────────────────────────────────────
    var config = builder.Configuration;

    // ── EF Core + MySQL ──────────────────────────────────────────
    var connStr = config.GetConnectionString("DefaultConnection")!;
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

    // ── Redis ────────────────────────────────────────────────────
    var redisConnStr = config.GetConnectionString("Redis")!;
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnStr));

    // ── HttpClient ───────────────────────────────────────────────
    builder.Services.AddHttpClient();

    // ── Services ─────────────────────────────────────────────────
    // osu! API：全局配额闸门。不按调用方区分 —— 被争抢的是这一个 client 的配额。
    builder.Services.AddSingleton<OsuApiService>();
    builder.Services.AddSingleton<IOsuApiService>(sp => new RateLimitedOsuApiService(
        sp.GetRequiredService<OsuApiService>(),
        new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("OsuApi:RateLimitPerMinute", 50),
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = config.GetValue("OsuApi:QueueLimit", 100),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        })));

    builder.Services.AddSingleton<IPpCalculatorService, PpCalculatorService>();
    builder.Services.AddSingleton<IBeatmapFileService, BeatmapFileService>();
    builder.Services.AddSingleton<IImageCacheService, ImageCacheService>();
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();

    // 渲染：浏览器在启动时预热（PlaywrightWarmupService），并发与超时由 GuardedRenderService 兜住。
    builder.Services.AddSingleton<IBrowserProvider, PlaywrightBrowserProvider>();
    builder.Services.AddHostedService<PlaywrightWarmupService>();
    builder.Services.AddSingleton<PlaywrightRenderer>();
    builder.Services.AddSingleton<IRenderService>(sp => new GuardedRenderService(
        sp.GetRequiredService<PlaywrightRenderer>(),
        maxConcurrency: config.GetValue("Rendering:MaxConcurrency", 4),
        timeout: TimeSpan.FromMilliseconds(config.GetValue("Rendering:TimeoutMs", 8000))));

    builder.Services.AddSingleton<HitCircleAPI.Rendering.ScoreTheme.DefaultScoreTheme>();
    builder.Services.AddSingleton<HitCircleAPI.Rendering.UserInfoTheme.DefaultUserInfoTheme>();
    builder.Services.AddSingleton<HitCircleAPI.Rendering.PerformanceAnalyzeTheme.PerformanceAnalyzeTheme>();
    builder.Services.AddSingleton<HitCircleAPI.Rendering.BeatmapTheme.DefaultBeatmapTheme>();

    // ── 异常处理 ─────────────────────────────────────────────────
    // RetryableException → 503 + Retry-After，其余交回默认 500。
    builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
    builder.Services.AddProblemDetails();

    // ── Controllers ──────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── OpenAPI / Scalar ─────────────────────────────────────────
    builder.Services.AddOpenApi(options =>
    {
        // .NET 9 的 OpenAPI 生成器不读 XML 文档注释，这一步把控制器注释接进去。
        options.AddXmlComments(typeof(Program).Assembly);

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();
            document.Components.SecuritySchemes["ApiKey"] = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Name = "access_token",
                Description = "API Key authentication via access_token header"
            };
            return Task.CompletedTask;
        });
    });

    // 原先的"按 IP 每分钟 20 次"全局限流已移除：
    // 1) 它按 RemoteIpAddress 分区，挂上反向代理后所有请求共用一个分区，
    //    实际会退化成"整个 API 20 次/分钟"；
    // 2) 它顺带承担了 osu! 配额的兜底职责，而那件事现在由 RateLimitedOsuApiService 负责；
    // 3) 渲染的资源保护由 GuardedRenderService 的并发上限负责。
    // 也就是说，稀缺资源各自就近管，中间件不再充当代理层。

    // ── 静态文件（wwwroot）───────────────────────────────────────
    // 静态文件支持由 WebApplication 自动提供，无需显式注册服务

    // ────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── 应用数据库迁移 ───────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DatabaseMigrationStartup.MigrateAsync(db);
    }

    // ── OpenAPI（仅开发环境）─────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.AddPreferredSecuritySchemes("ApiKey")
                   .AddApiKeyAuthentication("ApiKey", x => x.Value = "");
        });
    }

    // ── 中间件管道 ────────────────────────────────────────────────
    app.UseExceptionHandler();
    app.UseStaticFiles();
    app.UseMiddleware<ApiKeyMiddleware>();

    app.UseAuthorization();
    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
