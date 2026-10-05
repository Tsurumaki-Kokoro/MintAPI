using MintOsuApi;
using MintOsuApi.Enums;
using MintOsuApi.Models;
using Newtonsoft.Json;

namespace MintAPI.Services.MatchLive;

public static class MatchLiveMock
{
    public const int MatchId = 109975520;
    public static MatchResponse Build(int stage, DateTimeOffset now)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rendering", "MultiplayerTheme", "mock", "match-109975520.json");
        var original = JsonConvert.DeserializeObject<MatchResponse>(File.ReadAllText(path), OsuClient.BuildJsonSettings())!;
        var gameEvent = original.EventList.First(e => e.Game is not null);
        var game = gameEvent.Game!;
        // Reuse the captured map and six actual scores; only replay lifecycle and timestamps.
        var started = now.AddMinutes(-5);
        original.MatchInfo.StartTime = started;
        original.MatchInfo.EndTime = stage >= 3 ? now : null;
        original.EventList = [];
        original.FirstEventId = gameEvent.Id - 1;
        original.EventList.Add(new MatchEvent { Id = gameEvent.Id - 1, Timestamp = started,
            Detail = new MatchEventDetail { Type = MatchEventType.MatchCreated } });
        if (stage >= 1)
        {
            game.StartTime = started.AddSeconds(10);
            game.EndTime = stage >= 2 ? started.AddMinutes(3) : null;
            if (stage < 2) game.Scores = [];
            gameEvent.Timestamp = game.StartTime;
            original.EventList.Add(gameEvent);
        }
        if (stage >= 3) original.EventList.Add(new MatchEvent { Id = gameEvent.Id + 1, Timestamp = now,
            Detail = new MatchEventDetail { Type = MatchEventType.MatchDisbanded } });
        original.LatestEventId = original.EventList.Max(e => e.Id);
        original.CurrentGameId = stage == 1 ? game.Id : null;
        return original;
    }
}
