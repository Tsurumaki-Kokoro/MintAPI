namespace HitCircleAPI.Services;

/// <summary>渲染并发已满。调用方应退避重试，而不是排队等待。</summary>
public sealed class RenderBusyException() : RetryableException("渲染并发已满");

/// <summary>单次渲染超过了配置的超时。</summary>
public sealed class RenderTimeoutException() : RetryableException("渲染超时");

/// <summary>
/// 给渲染加上并发上限与超时。并发满时立刻失败而不是排队 —— 排队会把等待藏起来，
/// 而调用方（bot）自己知道队列深度，比这里更适合决定要不要等。
/// </summary>
public sealed class GuardedRenderService(
    IRenderService inner,
    int maxConcurrency,
    TimeSpan timeout) : IRenderService
{
    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);

    public async Task<byte[]> RenderHtmlAsync(string html, int width, int height)
    {
        if (!await _slots.WaitAsync(0))
            throw new RenderBusyException();

        try
        {
            return await inner.RenderHtmlAsync(html, width, height).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new RenderTimeoutException();
        }
        finally
        {
            _slots.Release();
        }
    }
}
