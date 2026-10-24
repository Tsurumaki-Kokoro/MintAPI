using System.Reflection;
using MySqlConnector;
using StackExchange.Redis;

namespace MintAPI.Services;

/// <summary>启动探测结果；不包含地址、路径或凭据。</summary>
public sealed class RuntimeComponentState
{
    public string? ChromiumVersion { get; set; }
    public string? PreviewVersion { get; set; }
}

public sealed class ServiceInformation(RuntimeComponentState components, IConfiguration configuration)
{
    public object Get()
    {
        var assembly = typeof(ServiceInformation).Assembly;
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x => x.Key, x => x.Value);
        var features = new List<string> { "help.image", "player.weekly", "score.compare", "player.watch.group", "score.history" };
        if (!configuration.GetValue("PlayerWatch:Enabled", true)) features.Remove("player.watch.group");
        if (components.PreviewVersion != null) features.Add("beatmap.preview");
        return new
        {
            name = "MintAPI", version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown",
            apiVersion = "1.0", commit = metadata.GetValueOrDefault("SourceRevisionId", "unknown"),
            builtAt = metadata.GetValueOrDefault("BuildTimestamp", "unknown"), features,
            gameModes = new[] { "osu", "taiko", "catch", "mania" }, themes = new[] { "default", "yaowan" },
            limits = new { bestPlayList = 20 },
            components = new
            {
                playwright = typeof(Microsoft.Playwright.Playwright).Assembly.GetName().Version?.ToString(),
                chromium = components.ChromiumVersion, preview = components.PreviewVersion
            }
        };
    }
}

public interface IReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

/// <summary>探测真实依赖，不访问 osu! 或执行写入。</summary>
public sealed class ReadinessProbe(IConfiguration configuration, IConnectionMultiplexer redis, IBrowserProvider browser,
    RuntimeComponentState components, ILogger<ReadinessProbe> logger) : IReadinessProbe
{
    private async Task CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var db = new MySqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await db.OpenAsync(cancellationToken);
        await using var query = new MySqlCommand("SELECT 1", db);
        await query.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (!browser.Browser.IsConnected || components.PreviewVersion == null) return false;
            var database = CheckDatabaseAsync(deadline.Token);
            var cache = redis.GetDatabase().PingAsync().WaitAsync(deadline.Token);
            await Task.WhenAll(database, cache);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Readiness dependency check failed");
            return false;
        }
    }
}
