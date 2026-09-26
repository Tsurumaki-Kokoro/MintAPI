namespace HitCircleAPI.Services;

public class ImageCacheService : IImageCacheService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageCacheService> _logger;
    private readonly string _cacheDir;

    public ImageCacheService(IHttpClientFactory httpClientFactory, ILogger<ImageCacheService> logger, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDir = config["CacheDir"] ?? "cache";
    }

    public async Task<byte[]> GetAvatarAsync(string avatarUrl, int userId)
    {
        var dir = Path.Combine(_cacheDir, "avatar");
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(avatarUrl.Split('?')[0]);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var filePath = Path.Combine(dir, $"{userId}{ext}");

        if (File.Exists(filePath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath)).TotalHours < 24)
            return await File.ReadAllBytesAsync(filePath);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var data = await client.GetByteArrayAsync(avatarUrl);
            await File.WriteAllBytesAsync(filePath, data);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to download avatar for user {UserId}: {Message}", userId, ex.Message);
            if (File.Exists(filePath))
                return await File.ReadAllBytesAsync(filePath);
            return [];
        }
    }

    public async Task<byte[]?> GetUserBackgroundAsync(int userId)
    {
        var dir = Path.Combine(_cacheDir, "user_bg");
        Directory.CreateDirectory(dir);

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var path = Path.Combine(dir, $"{userId}{ext}");
            if (File.Exists(path))
                return await File.ReadAllBytesAsync(path);
        }
        return null;
    }

    public async Task SaveUserBackgroundAsync(int userId, byte[] data)
    {
        var dir = Path.Combine(_cacheDir, "user_bg");
        Directory.CreateDirectory(dir);

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var old = Path.Combine(dir, $"{userId}{ext}");
            if (File.Exists(old)) File.Delete(old);
        }

        var filePath = Path.Combine(dir, $"{userId}.png");
        await File.WriteAllBytesAsync(filePath, data);
    }

    public async Task<byte[]> GetBadgeAsync(string badgeUrl, int userId, int index)
    {
        var dir = Path.Combine(_cacheDir, "badge", userId.ToString());
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, $"{index}.png");
        if (File.Exists(filePath))
            return await File.ReadAllBytesAsync(filePath);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var data = await client.GetByteArrayAsync(badgeUrl);
            await File.WriteAllBytesAsync(filePath, data);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to download badge {Index} for user {UserId}: {Message}", index, userId, ex.Message);
            if (File.Exists(filePath))
                return await File.ReadAllBytesAsync(filePath);
            throw;
        }
    }
}
