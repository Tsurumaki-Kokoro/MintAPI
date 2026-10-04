namespace MintAPI.Services;

/// <summary>渲染并发已满。</summary>
public sealed class RenderBusyException() : RetryableException("渲染并发已满");

/// <summary>单次渲染超过了配置的超时。</summary>
public sealed class RenderTimeoutException() : RetryableException("渲染超时");

/// <summary>限制渲染并发与执行时间。</summary>
public sealed class GuardedRenderService(
    IRenderService inner,
    int maxConcurrency,
    TimeSpan timeout) : IRenderService
{
    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);

    public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
    {
        if (!await _slots.WaitAsync(0))
            throw new RenderBusyException();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var detached = false;
        try
        {
            var renderTask = inner.RenderHtmlAsync(html, width, height, timeoutCts.Token);
            try
            {
                return await renderTask.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                detached = true;
                // Playwright will close its context. Until it actually stops, the render
                // still counts towards the concurrency limit.
                _ = ReleaseSlotWhenFinishedAsync(renderTask);
                timeoutCts.Cancel();
                throw new RenderTimeoutException();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                detached = true;
                _ = ReleaseSlotWhenFinishedAsync(renderTask);
                throw;
            }
        }
        finally
        {
            if (!detached)
                _slots.Release();
        }
    }

    private async Task ReleaseSlotWhenFinishedAsync(Task renderTask)
    {
        try
        {
            await renderTask;
        }
        catch
        {
            // The timeout response has already been sent. Observe the task's failure.
        }
        finally
        {
            _slots.Release();
        }
    }
}
