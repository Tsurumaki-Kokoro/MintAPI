using HitCircleAPI.Services;

namespace HitCircleAPI.Tests.TestDoubles;

/// <summary>
/// 一个会卡在渲染中的 <see cref="IRenderService"/>：每次进入渲染就放行一个 entered 信号，
/// 直到 <see cref="Release"/> 被调用才返回。用于观察并发行为。
/// </summary>
public sealed class BlockingRenderService : IRenderService
{
    private static readonly byte[] FakePng = [0x89, 0x50, 0x4E, 0x47];

    private readonly SemaphoreSlim _entered = new(0);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _cancelled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _current;
    private int _peak;

    /// <summary>曾经同时进入渲染的最大数量。</summary>
    public int PeakConcurrency => Volatile.Read(ref _peak);

    public async Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(() => _cancelled.TrySetResult());
        var current = Interlocked.Increment(ref _current);
        InterlockedMax(ref _peak, current);
        _entered.Release();

        try
        {
            await _release.Task;
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }

        return FakePng;
    }

    /// <summary>等到至少 <paramref name="count"/> 个渲染已经进入。</summary>
    public async Task WaitUntilEnteredAsync(int count)
    {
        for (var entered = 0; entered < count; entered++)
        {
            if (!await _entered.WaitAsync(TimeSpan.FromSeconds(5)))
                throw new TimeoutException($"只有 {entered} 个渲染进入，期望 {count} 个");
        }
    }

    public void Release() => _release.TrySetResult();

    public Task WaitUntilCancelledAsync() => _cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
                return;
        }
    }
}
