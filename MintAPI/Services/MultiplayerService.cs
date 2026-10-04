using MintOsuApi.Models;

namespace MintAPI.Services;

public sealed class MultiplayerService(IOsuApiService osuApi)
{
    public async Task<MatchResponse> GetCompleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
    {
        var result = await osuApi.GetMatchAsync(matchId, cancellationToken: cancellationToken);
        var events = result.EventList.ToDictionary(item => item.Id);
        var users = result.Users.ToDictionary(item => item.Id);
        while (events.Count > 0 && events.Keys.Min() > result.FirstEventId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = events.Keys.Min();
            var previous = await osuApi.GetMatchAsync(matchId, before, cancellationToken);
            if (previous.EventList.Count == 0 || previous.EventList.Min(item => item.Id) >= before)
                throw new InvalidOperationException("Incomplete match history returned by osu! API.");
            foreach (var item in previous.EventList) events.TryAdd(item.Id, item);
            foreach (var user in previous.Users) users.TryAdd(user.Id, user);
        }
        result.EventList = events.Values.OrderBy(item => item.Id).ToList();
        result.Users = users.Values.ToList();
        return result;
    }
}
