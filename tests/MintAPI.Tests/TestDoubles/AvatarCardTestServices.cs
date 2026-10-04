using MintAPI.Services;

namespace MintAPI.Tests.TestDoubles;

public sealed class AvatarCardImageCache(byte[] avatar) : IImageCacheService
{
    public Task<byte[]> GetAvatarAsync(string avatarUrl, int userId) => Task.FromResult(avatar);
    public Task<byte[]?> GetUserBackgroundAsync(int userId) => throw new NotSupportedException();
    public Task<byte[]?> GetUserBannerAsync(string? bannerUrl, int userId) => throw new NotSupportedException();
    public Task SaveUserBackgroundAsync(int userId, byte[] data) => throw new NotSupportedException();
    public Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index) => throw new NotSupportedException();
}

public sealed class AvatarCardRecordingRenderer : IRenderService
{
    public string? Html { get; private set; }
    public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
    {
        Html = html;
        Assert.Equal(512, width);
        Assert.Equal(512, height);
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4e, 0x47 });
    }
}
