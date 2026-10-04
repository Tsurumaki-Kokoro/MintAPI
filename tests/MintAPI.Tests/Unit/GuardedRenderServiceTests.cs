using MintAPI.Services;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Unit;

public class GuardedRenderServiceTests
{
    [Fact]
    public async Task RenderHtmlAsync_when_slots_exhausted_throws_RenderBusyException()
    {
        var inner = new BlockingRenderService();
        var guard = new GuardedRenderService(inner, maxConcurrency: 2, timeout: TimeSpan.FromSeconds(30));

        var inFlight = new[]
        {
            guard.RenderHtmlAsync("<html/>", 100, 100),
            guard.RenderHtmlAsync("<html/>", 100, 100),
        };
        await inner.WaitUntilEnteredAsync(2);

        await Assert.ThrowsAsync<RenderBusyException>(
            () => guard.RenderHtmlAsync("<html/>", 100, 100));

        inner.Release();
        await Task.WhenAll(inFlight);
    }

    [Fact]
    public async Task RenderHtmlAsync_never_runs_more_than_max_concurrency()
    {
        var inner = new BlockingRenderService();
        var guard = new GuardedRenderService(inner, maxConcurrency: 2, timeout: TimeSpan.FromSeconds(30));

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await guard.RenderHtmlAsync("<html/>", 100, 100);
                }
                catch (RenderBusyException)
                {
                    // 槽位满时抛错是预期行为，这里只关心峰值
                }
            }))
            .ToArray();

        await inner.WaitUntilEnteredAsync(2);
        await Task.Delay(200); // 给其余 6 个足够时间尝试进入

        Assert.Equal(2, inner.PeakConcurrency);

        inner.Release();
        await Task.WhenAll(attempts);
    }

    [Fact]
    public async Task RenderHtmlAsync_when_render_exceeds_timeout_throws_RenderTimeoutException()
    {
        var inner = new BlockingRenderService();
        var guard = new GuardedRenderService(inner, maxConcurrency: 2, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<RenderTimeoutException>(
            () => guard.RenderHtmlAsync("<html/>", 100, 100));
        await inner.WaitUntilCancelledAsync();
        inner.Release();
    }

    [Fact]
    public async Task RenderHtmlAsync_after_timeout_holds_slot_until_inner_render_stops()
    {
        var inner = new BlockingRenderService();
        var guard = new GuardedRenderService(inner, maxConcurrency: 1, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<RenderTimeoutException>(
            () => guard.RenderHtmlAsync("<html/>", 100, 100));
        await inner.WaitUntilCancelledAsync();

        await Assert.ThrowsAsync<RenderBusyException>(
            () => guard.RenderHtmlAsync("<html/>", 100, 100));

        inner.Release();
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            wait.Token.ThrowIfCancellationRequested();
            try
            {
                await guard.RenderHtmlAsync("<html/>", 100, 100);
                break;
            }
            catch (RenderBusyException)
            {
                await Task.Delay(10, wait.Token);
            }
        }
        Assert.Equal(1, inner.PeakConcurrency);
    }
}
