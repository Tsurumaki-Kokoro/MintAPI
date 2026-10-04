using Microsoft.Playwright;

namespace MintAPI.Services;

/// <summary>管理进程级 Playwright 浏览器实例。</summary>
public sealed class PlaywrightBrowserProvider(ILogger<PlaywrightBrowserProvider> logger) : IBrowserProvider
{
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException(
        "浏览器尚未启动。应由 PlaywrightWarmupService 在应用启动时调用 StartAsync。");

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_browser is not null) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is not null) return;

            // 浏览器本体只由构建期安装（Dockerfile）。运行期不再调用 playwright install ——
            // 那会在请求路径里下载上百 MB。
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
            });

            logger.LogInformation("Playwright 浏览器已启动");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _playwright?.Dispose();
    }
}
