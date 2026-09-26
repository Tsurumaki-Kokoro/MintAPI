using HitCircleAPI.Services;
using Microsoft.Playwright;

namespace HitCircleAPI.Tests.TestDoubles;

/// <summary>记录 <see cref="StartAsync"/> 调用次数，可注入启动失败。</summary>
public sealed class RecordingBrowserProvider : IBrowserProvider
{
    private int _startCount;

    public int StartCount => Volatile.Read(ref _startCount);

    public Exception? StartFailure { get; init; }

    public IBrowser Browser => throw new NotSupportedException("测试替身不提供浏览器实例");

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _startCount);

        return StartFailure is null
            ? Task.CompletedTask
            : Task.FromException(StartFailure);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
