using MintOsuApi;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace MintAPI.Services.MatchLive;

public interface IMatchLiveStore
{
    Task<IReadOnlyList<LiveRoom>> LoadAsync(CancellationToken ct);
    Task SaveAsync(LiveRoom room, CancellationToken ct);
    Task DeleteAsync(int matchId, CancellationToken ct);
}

public sealed class RedisMatchLiveStore(IConnectionMultiplexer redis, IHostEnvironment environment) : IMatchLiveStore
{
    private string Prefix => $"mintapi:matchlive:{environment.EnvironmentName.ToLowerInvariant()}";
    private string Index => $"{Prefix}:rooms";
    private string Key(int id) => $"{Prefix}:room:{id}";
    public async Task<IReadOnlyList<LiveRoom>> LoadAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var rooms = new List<LiveRoom>();
        foreach (var id in await db.SetMembersAsync(Index).WaitAsync(ct))
        {
            if (!int.TryParse(id.ToString(), out var number)) continue;
            var data = await db.StringGetAsync(Key(number)).WaitAsync(ct);
            if (data.HasValue)
            {
                var room = JsonConvert.DeserializeObject<LiveRoom>(data.ToString(), OsuClient.BuildJsonSettings());
                if (room is not null) rooms.Add(room);
            }
            else await db.SetRemoveAsync(Index, id).WaitAsync(ct);
        }
        return rooms;
    }

    public async Task SaveAsync(LiveRoom room, CancellationToken ct)
    {
        var transaction = redis.GetDatabase().CreateTransaction();
        _ = transaction.StringSetAsync(Key(room.MatchId), JsonConvert.SerializeObject(room, OsuClient.BuildJsonSettings()), TimeSpan.FromDays(2));
        _ = transaction.SetAddAsync(Index, room.MatchId);
        if (!await transaction.ExecuteAsync().WaitAsync(ct)) throw new InvalidOperationException("Could not save match live state.");
    }

    public async Task DeleteAsync(int matchId, CancellationToken ct)
    {
        var transaction = redis.GetDatabase().CreateTransaction();
        _ = transaction.KeyDeleteAsync(Key(matchId));
        _ = transaction.SetRemoveAsync(Index, matchId);
        await transaction.ExecuteAsync().WaitAsync(ct);
    }
}
