using System.Threading.RateLimiting;

namespace HitCircleAPI.Tests.TestDoubles;

public enum LimiterMode
{
    /// <summary>立刻放行。</summary>
    Grant,

    /// <summary>挂起，直到 <see cref="ControllableRateLimiter.Release"/>。</summary>
    Block,

    /// <summary>立刻拒绝（对应真实限流器队列也满了的情况）。</summary>
    Deny,
}

/// <summary>
/// 可控的 <see cref="RateLimiter"/>。用来确定性地观察调用方是否真的在等配额，
/// 而不是靠 sleep 赌时间。
/// </summary>
public sealed class ControllableRateLimiter : RateLimiter
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _acquireCount;
    private int _mode = (int)LimiterMode.Grant;

    public int AcquireCount => Volatile.Read(ref _acquireCount);

    public void SetMode(LimiterMode mode) => Volatile.Write(ref _mode, (int)mode);

    public void Release() => _gate.TrySetResult();

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => LeaseForCurrentMode();

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _acquireCount);

        return (LimiterMode)Volatile.Read(ref _mode) switch
        {
            LimiterMode.Block => new ValueTask<RateLimitLease>(WaitForGateAsync(cancellationToken)),
            _ => ValueTask.FromResult(LeaseForCurrentMode()),
        };
    }

    private RateLimitLease LeaseForCurrentMode()
        => (LimiterMode)Volatile.Read(ref _mode) == LimiterMode.Deny
            ? DeniedLease.Instance
            : GrantedLease.Instance;

    private async Task<RateLimitLease> WaitForGateAsync(CancellationToken cancellationToken)
    {
        await _gate.Task.WaitAsync(cancellationToken);
        return GrantedLease.Instance;
    }

    protected override void Dispose(bool disposing) { }

    private sealed class GrantedLease : RateLimitLease
    {
        public static readonly GrantedLease Instance = new();

        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }

    private sealed class DeniedLease : RateLimitLease
    {
        public static readonly DeniedLease Instance = new();

        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
