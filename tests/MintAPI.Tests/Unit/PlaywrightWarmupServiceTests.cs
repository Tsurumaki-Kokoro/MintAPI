using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace MintAPI.Tests.Unit;

public class PlaywrightWarmupServiceTests
{
    [Fact]
    public async Task StartAsync_starts_the_browser_provider()
    {
        var provider = new RecordingBrowserProvider();
        var warmup = new PlaywrightWarmupService(provider, NullLogger<PlaywrightWarmupService>.Instance);

        await warmup.StartAsync(CancellationToken.None);

        Assert.Equal(1, provider.StartCount);
    }

    [Fact]
    public async Task StartAsync_propagates_launch_failure_so_the_app_fails_fast()
    {
        var provider = new RecordingBrowserProvider { StartFailure = new InvalidOperationException("chromium 缺失") };
        var warmup = new PlaywrightWarmupService(provider, NullLogger<PlaywrightWarmupService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => warmup.StartAsync(CancellationToken.None));

        Assert.Equal("chromium 缺失", ex.Message);
    }
}
