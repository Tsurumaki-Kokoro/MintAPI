using Microsoft.Playwright;

namespace HitCircleAPI.Services;

/// <summary>持有进程级共享的浏览器实例。</summary>
public interface IBrowserProvider : IAsyncDisposable
{
    /// <summary>幂等。启动失败直接抛出，让调用方决定是否放弃启动。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>未启动时抛 <see cref="InvalidOperationException"/>，不隐式惰性启动。</summary>
    IBrowser Browser { get; }
}
