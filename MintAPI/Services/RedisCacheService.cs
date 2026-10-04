using StackExchange.Redis;

namespace MintAPI.Services;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;

    public RedisCacheService(IConnectionMultiplexer redis) => _db = redis.GetDatabase();

    public async Task<string?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.HasValue ? (string?)value : null;
    }

    public Task SetAsync(string key, string value, TimeSpan? expiry = null)
        => _db.StringSetAsync(key, value, expiry);

    public Task<bool> DeleteAsync(string key)
        => _db.KeyDeleteAsync(key);

    public Task<bool> ExistsAsync(string key)
        => _db.KeyExistsAsync(key);
}
