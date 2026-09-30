using HitCircleAPI.Services;
using HitCircleAPI.Tests.TestDoubles;
using Ossapi.Models;

namespace HitCircleAPI.Tests.Unit;

public class MultiplayerServiceTests
{
    [Fact]
    public async Task Complete_history_fetches_older_events_and_merges_users()
    {
        var api = new RecordingOsuApiService
        {
            MatchHandler = before => before == null
                ? new MatchResponse { FirstEventId = 1, EventList = [new MatchEvent { Id = 3 }, new MatchEvent { Id = 4 }], Users = [new UserCompact { Id = 2 }] }
                : new MatchResponse { EventList = [new MatchEvent { Id = 1 }, new MatchEvent { Id = 2 }, new MatchEvent { Id = 3 }], Users = [new UserCompact { Id = 1 }] }
        };
        var match = await new MultiplayerService(api).GetCompleteMatchAsync(123);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, match.EventList.Select(item => item.Id));
        Assert.Equal(2, match.Users.Count);
        Assert.Equal(2, api.CallCount);
    }

    [Fact]
    public async Task Large_event_ids_are_preserved_in_pagination_and_ordering()
    {
        const long firstId = 2272624578;
        const long beforeId = 2272624580;
        var cursors = new List<long?>();
        var api = new RecordingOsuApiService
        {
            MatchHandler = before =>
            {
                cursors.Add(before);
                return before == null
                    ? new MatchResponse { FirstEventId = firstId, LatestEventId = beforeId + 1,
                        EventList = [new MatchEvent { Id = beforeId }, new MatchEvent { Id = beforeId + 1 }] }
                    : new MatchResponse { EventList = [new MatchEvent { Id = firstId }, new MatchEvent { Id = firstId + 1 }] };
            }
        };
        var match = await new MultiplayerService(api).GetCompleteMatchAsync(109850222);
        Assert.Equal(new long?[] { null, beforeId }, cursors);
        Assert.Equal(new[] { firstId, firstId + 1, beforeId, beforeId + 1 }, match.EventList.Select(item => item.Id));
        Assert.Equal(beforeId + 1, match.LatestEventId);
    }

    [Fact]
    public async Task Nonadvancing_pagination_fails_instead_of_returning_partial_statistics()
    {
        var api = new RecordingOsuApiService
        {
            MatchHandler = _ => new MatchResponse { FirstEventId = 1, EventList = [new MatchEvent { Id = 3 }] }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MultiplayerService(api).GetCompleteMatchAsync(123));
        Assert.Equal(2, api.CallCount);
    }
}
