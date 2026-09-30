using Newtonsoft.Json;
using Ossapi.Models;

namespace Ossapi.Tests.Unit;

public class MatchScoreTests
{
    [Fact]
    public void Match_response_reads_object_mods_on_game_and_score()
    {
        const string json = """
            {"events":[{"id":2272624580,"game":{"mods":[{"acronym":"HD","settings":{}}],"scores":[{"user_id":42,"total_score":987654,"legacy_total_score":100000000,"mods":[{"acronym":"HR","settings":{}}]}]}}]}
            """;
        var match = JsonConvert.DeserializeObject<MatchResponse>(json)!;
        var game = Assert.Single(match.EventList).Game!;
        Assert.Equal("HD", game.Mods.ToShortName());
        var score = Assert.Single(game.Scores);
        Assert.Equal("HR", score.Mods.ToShortName());
        Assert.Equal(987654, score.Score);
    }

    [Fact]
    public void Match_response_reads_event_ids_larger_than_int32()
    {
        const string json = """
            {"match":{"id":109850222},"events":[{"id":2272624580}],"first_event_id":2272624578,"latest_event_id":2272624581}
            """;
        var match = JsonConvert.DeserializeObject<MatchResponse>(json)!;
        Assert.Equal(2272624580L, Assert.Single(match.EventList).Id);
        Assert.Equal(2272624578L, match.FirstEventId);
        Assert.Equal(2272624581L, match.LatestEventId);
    }

    [Fact]
    public void Match_response_reads_legacy_score_and_mod_array()
    {
        const string json = """
            {"events":[{"id":1,"game":{"scores":[{"user_id":42,"score":987654,"accuracy":0.99,"mods":["HD","HR"],"match":{"team":"red","pass":true}}]}}]}
            """;
        var match = JsonConvert.DeserializeObject<MatchResponse>(json)!;
        var score = Assert.Single(Assert.Single(match.EventList).Game!.Scores);
        Assert.Equal(987654, score.Score);
        Assert.Equal("HDHR", score.Mods.ToShortName());
        Assert.True(score.Match!.Pass);
    }
}
