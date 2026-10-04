using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;

namespace MintAPI.Tests.Unit;

public class RateLimitedOsuApiServiceTests
{
    [Fact]
    public async Task GetUserAsync_when_permit_available_calls_the_api()
    {
        var inner = new RecordingOsuApiService();
        using var limiter = new ControllableRateLimiter();
        var service = new RateLimitedOsuApiService(inner, limiter);

        await service.GetUserAsync("u");

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetUserAsync_waits_for_a_permit_before_calling_the_api()
    {
        var inner = new RecordingOsuApiService();
        using var limiter = new ControllableRateLimiter();
        var service = new RateLimitedOsuApiService(inner, limiter);

        limiter.SetMode(LimiterMode.Block);
        var call = service.GetUserAsync("u");

        // 装饰器会同步跑到申请配额那一步才挂起，所以这里无需 sleep 也能确定观察
        Assert.Equal(1, limiter.AcquireCount);
        Assert.False(call.IsCompleted);
        Assert.Equal(0, inner.CallCount);

        limiter.Release();
        await call;

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetUserAsync_when_quota_exhausted_throws_OsuQuotaExceededException()
    {
        var inner = new RecordingOsuApiService();
        using var limiter = new ControllableRateLimiter();
        var service = new RateLimitedOsuApiService(inner, limiter);

        limiter.SetMode(LimiterMode.Deny);

        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => service.GetUserAsync("u"));

        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task every_interface_method_acquires_a_permit()
    {
        var inner = new RecordingOsuApiService();
        using var limiter = new ControllableRateLimiter();
        var service = new RateLimitedOsuApiService(inner, limiter);

        await service.GetUserAsync("u");
        await service.GetUserScoresAsync(1, ScoreType.Best);
        await service.GetBeatmapAsync(1);
        await service.GetBeatmapsetAsync(1);
        await service.GetBeatmapUserScoresAsync(1, 1);
        await service.GetSeasonalBackgroundsAsync();

        Assert.Equal(6, limiter.AcquireCount);
        Assert.Equal(6, inner.CallCount);
    }
}
