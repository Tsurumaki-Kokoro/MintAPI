namespace MintAPI.Services;

/// <summary>应用启动时预热浏览器。</summary>
public sealed class PlaywrightWarmupService(
    IBrowserProvider browserProvider,
    ILogger<PlaywrightWarmupService> logger, RuntimeComponentState? components = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("正在预热 Playwright 浏览器…");
        await browserProvider.StartAsync(cancellationToken);
        if (components != null) components.ChromiumVersion = browserProvider.Browser.Version;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
