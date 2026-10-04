namespace MintAPI.Services;

/// <summary>应用启动时预热浏览器。</summary>
public sealed class PlaywrightWarmupService(
    IBrowserProvider browserProvider,
    ILogger<PlaywrightWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("正在预热 Playwright 浏览器…");
        await browserProvider.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
