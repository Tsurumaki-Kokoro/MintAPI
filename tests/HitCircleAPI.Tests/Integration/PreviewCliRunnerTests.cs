using HitCircleAPI.Services.Preview;

namespace HitCircleAPI.Tests.Integration;

public sealed class PreviewCliRunnerTests
{
    [Fact]
    public async Task Cancellation_terminates_running_process()
    {
        // Unix process lifecycle acceptance for macOS/Linux; Windows uses the same Kill tree API.
        if (OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var runner = new PreviewCliRunner();
        var started = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync("/bin/sleep", ["60"], cancellation.Token));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
