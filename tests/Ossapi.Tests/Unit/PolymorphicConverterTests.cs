using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;
using Ossapi.Models;
using Xunit;

namespace Ossapi.Tests.Unit;

public class PolymorphicConverterTests
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Converters =
        {
            new EventConverter(),
            new BeatmapsetEventConverter(),
            new GameModeConverter(),
            new EventTypeConverter(),
            new BeatmapsetEventTypeConverter(),
            new DateTimeOffsetRequiredConverter(),
            new KudosuActionConverter(),
        },
    };

    private static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)!;

    // ---------------------------------------------------------------------------
    // EventConverter
    // ---------------------------------------------------------------------------

    [Fact]
    public void EventConverter_Deserializes_AchievementEvent()
    {
        var json = """
            {
                "id": 1,
                "type": "achievement",
                "created_at": "2021-01-01T00:00:00+00:00",
                "achievement": { "icon_url": "", "id": 42, "name": "Finality", "grouping": "Skill", "ordering": 1, "slug": "finality", "description": "", "mode": null, "instructions": null },
                "user": { "username": "TestUser", "id": 123 }
            }
            """;

        var result = Deserialize<Event>(json);

        Assert.IsType<AchievementEvent>(result);
        var ev = (AchievementEvent)result;
        Assert.Equal(1, ev.Id);
        Assert.Equal(EventType.Achievement, ev.Type);
        Assert.Equal("TestUser", ev.User.Username);
    }

    [Fact]
    public void EventConverter_Deserializes_RankEvent()
    {
        var json = """
            {
                "id": 2,
                "type": "rank",
                "created_at": "2021-06-15T12:00:00+00:00",
                "scoreRank": "S",
                "rank": 500,
                "mode": "osu",
                "beatmap": { "title": "song", "url": "/beatmaps/1" },
                "user":    { "username": "player", "id": 9 }
            }
            """;

        var result = Deserialize<Event>(json);

        Assert.IsType<RankEvent>(result);
        var ev = (RankEvent)result;
        Assert.Equal(GameMode.Osu, ev.Mode);
        Assert.Equal(500, ev.Rank);
    }

    [Fact]
    public void EventConverter_Deserializes_BeatmapPlaycountEvent()
    {
        var json = """
            {
                "id": 3,
                "type": "beatmapPlaycount",
                "created_at": "2022-03-10T08:00:00+00:00",
                "count": 1000,
                "beatmap": { "title": "map", "url": "/beatmaps/2" }
            }
            """;

        var result = Deserialize<Event>(json);

        Assert.IsType<BeatmapPlaycountEvent>(result);
        Assert.Equal(1000, ((BeatmapPlaycountEvent)result).Count);
    }

    [Fact]
    public void EventConverter_Deserializes_List_Of_Events()
    {
        var json = """
            [
                { "id": 1, "type": "achievement", "created_at": "2021-01-01T00:00:00+00:00",
                  "achievement": { "icon_url":"","id":1,"name":"x","grouping":"g","ordering":0,"slug":"x","description":"","mode":null,"instructions":null },
                  "user": { "username": "a", "id": 1 } },
                { "id": 2, "type": "rank", "created_at": "2021-01-02T00:00:00+00:00",
                  "scoreRank": "A", "rank": 1, "mode": "taiko",
                  "beatmap": { "title": "t", "url": "/beatmaps/1" },
                  "user": { "username": "b", "id": 2 } }
            ]
            """;

        var results = Deserialize<List<Event>>(json);

        Assert.Equal(2, results.Count);
        Assert.IsType<AchievementEvent>(results[0]);
        Assert.IsType<RankEvent>(results[1]);
    }

    // ---------------------------------------------------------------------------
    // BeatmapsetEventConverter
    // ---------------------------------------------------------------------------

    [Fact]
    public void BeatmapsetEventConverter_Deserializes_KudosuGain_Comment()
    {
        var json = """
            {
                "id": 10,
                "type": "kudosu_gain",
                "created_at": "2021-05-01T00:00:00+00:00",
                "user_id": 7,
                "comment": {
                    "beatmap_discussion_id": 5,
                    "beatmap_discussion_post_id": null,
                    "new_vote": { "user_id": 7, "score": 1 },
                    "votes": [ { "user_id": 7, "score": 1 } ]
                }
            }
            """;

        var result = Deserialize<BeatmapsetEvent>(json);

        Assert.Equal(BeatmapsetEventType.KudosuGain, result.Type);
        var comment = Assert.IsType<BeatmapsetEventCommentKudosuChange>(result.Comment);
        Assert.Equal(1, comment.NewVote.Score);
    }

    [Fact]
    public void BeatmapsetEventConverter_Deserializes_Nominate_Comment()
    {
        var json = """
            {
                "id": 20,
                "type": "nominate",
                "created_at": "2021-05-02T00:00:00+00:00",
                "user_id": 9,
                "comment": {
                    "modes": ["osu", "taiko"]
                }
            }
            """;

        var result = Deserialize<BeatmapsetEvent>(json);

        Assert.Equal(BeatmapsetEventType.Nominate, result.Type);
        var comment = Assert.IsType<BeatmapsetEventCommentNominate>(result.Comment);
        Assert.Contains(GameMode.Osu, comment.Modes);
        Assert.Contains(GameMode.Taiko, comment.Modes);
    }

    [Fact]
    public void BeatmapsetEventConverter_Handles_String_Comment_For_Disqualify()
    {
        var json = """
            {
                "id": 30,
                "type": "disqualify",
                "created_at": "2021-05-03T00:00:00+00:00",
                "user_id": 5,
                "comment": "This map needs more work."
            }
            """;

        var result = Deserialize<BeatmapsetEvent>(json);

        Assert.Equal(BeatmapsetEventType.Disqualify, result.Type);
        Assert.Equal("This map needs more work.", result.Comment);
    }

    [Fact]
    public void BeatmapsetEventConverter_Handles_Null_Comment()
    {
        var json = """
            {
                "id": 40,
                "type": "qualify",
                "created_at": "2021-05-04T00:00:00+00:00",
                "user_id": null,
                "comment": null
            }
            """;

        var result = Deserialize<BeatmapsetEvent>(json);

        Assert.Equal(BeatmapsetEventType.Qualify, result.Type);
        Assert.Null(result.Comment);
    }

    [Fact]
    public void BeatmapsetEventConverter_Deserializes_GenreEdit_Comment()
    {
        var json = """
            {
                "id": 50,
                "type": "genre_edit",
                "created_at": "2021-06-01T00:00:00+00:00",
                "user_id": 3,
                "comment": {
                    "beatmap_discussion_id": null,
                    "beatmap_discussion_post_id": null,
                    "old": "Unspecified",
                    "new": "Video Game"
                }
            }
            """;

        var result = Deserialize<BeatmapsetEvent>(json);

        var comment = Assert.IsType<BeatmapsetEventCommentChange>(result.Comment);
        Assert.Equal("Unspecified", comment.Old?.ToString());
        Assert.Equal("Video Game", comment.New?.ToString());
    }
}
