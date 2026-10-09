using MintAPI.Configuration;
using Microsoft.Extensions.Options;
using MintAPI.Services.Preview;
using MintAPI.Services.MatchLive;
using MintAPI.Data;
using MintAPI.Middleware;
using MintAPI.OpenApi;
using MintAPI.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Services.AddOperationalConfiguration(builder.Configuration, builder.Environment);
    Log.Logger = OperationalConfiguration.CreateLogger(builder.Configuration,
        new StoragePaths(builder.Configuration, builder.Environment));

    builder.Host.UseSerilog();
    var config = builder.Configuration;
    var connStr = config.GetConnectionString("DefaultConnection")!;
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

    var redisConnStr = config.GetConnectionString("Redis")!;
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        ConnectionMultiplexer.Connect(redisConnStr));

    builder.Services.AddHttpClient();
    // Each download supplies its own total deadline, including response body reads.
    builder.Services.AddHttpClient("Downloads", client => client.Timeout = Timeout.InfiniteTimeSpan);
    builder.Services.AddHttpClient("OsuTrack", (services, client) =>
    {
        var downloads = services.GetRequiredService<IOptions<DownloadOptions>>().Value;
        client.BaseAddress = new Uri(downloads.OsuTrackBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(downloads.OsuTrackTimeoutSeconds);
    });
    builder.Services.AddMemoryCache();
    builder.Services.AddOptions<HistoryOptions>().Bind(config.GetSection("History"))
        .Validate(options => options.IsValid(), "History requires a valid time zone, HH:mm times, ScoreTime >= InfoTime, and valid limits.")
        .ValidateOnStart();
    builder.Services.AddScoped<HistoryService>();
    builder.Services.AddScoped<HistoryCollector>();
    builder.Services.AddSingleton<HistoryRenderer>();
    builder.Services.AddSingleton<UserRankingRenderer>();
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
    builder.Services.AddSingleton<MintAPI.Rendering.BeatmapSearchTheme.BeatmapSearchTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.BeatmapTheme.DefaultBeatmapTheme>();
    builder.Services.AddSingleton<MintAPI.Rendering.BeatmapTheme.BeatmapBpmTheme>();
    builder.Services.AddSingleton<MultiplayerService>();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddOptions<MatchLiveOptions>().Bind(config.GetSection("MatchLive"))
        .PostConfigure(options => options.MockEnabled &= builder.Environment.IsDevelopment())
        .Validate(options => options.IsValid() && options.RequestBudgetPerMinute < config.GetValue("OsuApi:RateLimitPerMinute", 50),
            "Invalid MatchLive limits, intervals, or query budget (must be below the global limit).").ValidateOnStart();
    builder.Services.AddSingleton<IMatchLiveStore, RedisMatchLiveStore>();
    builder.Services.AddSingleton<MatchLiveService>();
    builder.Services.AddHostedService<MatchLiveWorker>();
    builder.Services.AddSingleton<MintAPI.Rendering.MultiplayerTheme.MultiplayerTheme>();

    builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddControllers(options => options.Filters.Add<MintAPI.Errors.ApiErrorFilter>());
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

    app.Use(async (context, next) =>
    {
        using var scope = app.Logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier
        });
        await next(context);
    });
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
