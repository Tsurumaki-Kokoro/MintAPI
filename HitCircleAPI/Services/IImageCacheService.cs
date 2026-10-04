namespace HitCircleAPI.Services;

public interface IImageCacheService
{
    Task<byte[]> GetAvatarAsync(string avatarUrl, int userId);
    Task<byte[]?> GetUserBackgroundAsync(int userId);
    Task<byte[]?> GetUserBannerAsync(string? bannerUrl, int userId);
    Task SaveUserBackgroundAsync(int userId, byte[] data);
    Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index);
}
