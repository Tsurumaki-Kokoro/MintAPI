using MintAPI.Services.Preview;
using MintAPI.Data;
using MintAPI.Middleware;
using MintAPI.OpenApi;
using MintAPI.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using System.Threading.RateLimiting;

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

    builder.Host.UseSerilog();
    var config = builder.Configuration;
    var connStr = config.GetConnectionString("DefaultConnection")!;
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

    var redisConnStr = config.GetConnectionString("Redis")!;
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnStr));

    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient("OsuTrack", client =>
    {
        client.BaseAddress = new Uri("https://osutrack-api.ameo.dev/");
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddMemoryCache();
    builder.Services.AddOptions<HistoryOptions>().Bind(config.GetSection("History"))
        .Validate(options => options.IsValid(), "History requires a valid time zone, HH:mm times, ScoreTime >= InfoTime, and valid limits.")
        .ValidateOnStart();
    builder.Services.AddScoped<HistoryService>();
    builder.Services.AddScoped<HistoryCollector>();
    builder.Services.AddSingleton<HistoryRenderer>();
    builder.Services.AddHostedService<HistoryScheduler>();

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
    builder.Services.AddSingleton<BpFixService>();
    builder.Services.AddSingleton<IBeatmapAnalysisService, BeatmapAnalysisService>();
    builder.Services.AddSingleton<IBeatmapFileService, BeatmapFileService>();
    builder.Services.AddOptions<BeatmapPreviewOptions>().Bind(config.GetSection("BeatmapPreview"))
        .Validate(options => options.IsValid(), "Invalid preview executable, concurrency, timeout or duration limits.")
        .ValidateOnStart();
    builder.Services.AddSingleton<IPreviewCliRunner, PreviewCliRunner>();
    builder.Services.AddSingleton<IBeatmapPreviewService, BeatmapPreviewService>();
    builder.Services.AddHostedService<PreviewCliWarmupService>();
    builder.Services.AddSingleton<IImageCacheService, ImageCacheService>();
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();

    builder.Services.AddSingleton<IBrowserProvider, PlaywrightBrowserProvider>();
    builder.Services.AddHostedService<PlaywrightWarmupService>();
    builder.Services.AddSingleton<PlaywrightRenderer>();
    builder.Services.AddSingleton<IRenderService>(sp => new GuardedRenderService(
        sp.GetRequiredService<PlaywrightRenderer>(),
        maxConcurrency: config.GetValue("Rendering:MaxConcurrency", 4),
        timeout: TimeSpan.FromMilliseconds(config.GetValue("Rendering:TimeoutMs", 8000))));

    builder.Services.AddSingleton<MintAPI.Rendering.AvatarCardTheme.AvatarCardTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.ScoreTheme.DefaultScoreTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.UserInfoTheme.DefaultUserInfoTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.PerformanceAnalyzeTheme.PerformanceAnalyzeTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.BeatmapTheme.DefaultBeatmapTheme>();
    builder.Services.AddSingleton<MultiplayerService>();
    builder.Services.AddSingleton<MintAPI.Rendering.MultiplayerTheme.MultiplayerTheme>();

    builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddControllers();
    builder.Services.AddOpenApi(options =>
    {
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

    var app = builder.Build();
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await DatabaseMigrationStartup.MigrateAsync(db);
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.AddPreferredSecuritySchemes("ApiKey")
                   .AddApiKeyAuthentication("ApiKey", x => x.Value = "");
        });
    }

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
