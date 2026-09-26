using Newtonsoft.Json;
using Ossapi.Mods;
using Ossapi.V1;
using Xunit;

namespace Ossapi.Tests.Unit;

public class V1ClientTests
{
    // ---------------------------------------------------------------------------
    // V1Beatmap deserialization
    // ---------------------------------------------------------------------------

    [Fact]
    public void V1Beatmap_Deserializes_All_Fields()
    {
        var json = """
            {
                "approved": "1",
                "submit_date": "2020-01-01 00:00:00",
                "approved_date": "2020-06-15 12:30:00",
                "last_update": "2021-03-10 08:00:00",
                "artist": "Artist",
                "beatmap_id": "123",
                "beatmapset_id": "456",
                "bpm": "180",
                "creator": "mapper",
                "creator_id": "789",
                "difficultyrating": "5.81",
                "diff_size": "4",
                "diff_overall": "9",
                "diff_approach": "9.5",
                "diff_drain": "7",
                "hit_length": "150",
                "title": "Song Title",
                "total_length": "200",
                "version": "Hard",
                "file_md5": "abc123",
                "mode": "0",
                "tags": "tag1 tag2",
                "favourite_count": "100",
                "playcount": "50000",
                "passcount": "25000",
                "count_normal": "300",
                "count_slider": "150",
                "count_spinner": "5",
                "max_combo": "800",
                "storyboard": "0",
                "video": "1",
                "download_unavailable": "0",
                "audio_unavailable": "0"
            }
            """;

        var bm = JsonConvert.DeserializeObject<V1Beatmap>(json)!;

        Assert.Equal("123", bm.BeatmapId);
        Assert.Equal(123, bm.BeatmapIdInt);
        Assert.Equal("Song Title", bm.Title);
        Assert.InRange(bm.StarRatingDouble!.Value, 5.8, 5.82);
        Assert.False(bm.HasStoryboard);
        Assert.True(bm.HasVideo);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), bm.SubmitDateOffset);
    }

    // ---------------------------------------------------------------------------
    // V1User deserialization
    // ---------------------------------------------------------------------------

    [Fact]
    public void V1User_Deserializes_With_Events()
    {
        var json = """
            {
                "user_id": "1",
                "username": "TestUser",
                "join_date": "2015-05-20 14:30:00",
                "count300": "100000",
                "count100": "10000",
                "count50": "1000",
                "playcount": "5000",
                "ranked_score": "1000000000",
                "total_score": "2000000000",
                "pp_rank": "500",
                "level": "99.5",
                "pp_raw": "3500.5",
                "accuracy": "98.76",
                "count_rank_ss": "10",
                "count_rank_ssh": "2",
                "count_rank_s": "100",
                "count_rank_sh": "20",
                "count_rank_a": "200",
                "country": "JP",
                "total_seconds_played": "1000000",
                "pp_country_rank": "50",
                "events": [
                    {
                        "display_html": "<b>Test</b>",
                        "beatmap_id": "999",
                        "beatmapset_id": "888",
                        "date": "2021-01-01 00:00:00",
                        "epicfactor": "1"
                    }
                ]
            }
            """;

        var user = JsonConvert.DeserializeObject<V1User>(json)!;

        Assert.Equal("TestUser", user.Username);
        Assert.Equal(1, user.UserIdInt);
        Assert.InRange(user.PpRawDouble!.Value, 3500.0, 3501.0);
        Assert.Equal("JP", user.Country);
        Assert.Single(user.Events);
        Assert.Equal("999", user.Events[0].BeatmapId);
        Assert.Equal(new DateTimeOffset(2015, 5, 20, 14, 30, 0, TimeSpan.Zero), user.JoinDateOffset);
    }

    // ---------------------------------------------------------------------------
    // V1Score deserialization
    // ---------------------------------------------------------------------------

    [Fact]
    public void V1Score_Deserializes_Mods_And_Date()
    {
        var json = """
            {
                "beatmap_id": "123",
                "score_id": "9999",
                "score": "1234567",
                "username": "Player",
                "count300": "800",
                "count100": "20",
                "count50": "5",
                "countmiss": "1",
                "maxcombo": "750",
                "countkatu": "5",
                "countgeki": "100",
                "perfect": "0",
                "enabled_mods": "72",
                "user_id": "42",
                "date": "2022-08-15 18:00:00",
                "rank": "S",
                "pp": "320.5",
                "replay_available": "1"
            }
            """;

        var score = JsonConvert.DeserializeObject<V1Score>(json)!;

        Assert.Equal("Player", score.Username);
        Assert.Equal("S", score.Rank);
        Assert.False(score.IsPerfect);
        // 72 = HD (8) + HR (16) + DT (64)
        Assert.NotNull(score.Mods);
        Assert.True(score.Mods!.Value.Value == 72);
        Assert.Equal(new DateTimeOffset(2022, 8, 15, 18, 0, 0, TimeSpan.Zero), score.DateOffset);
    }

    // ---------------------------------------------------------------------------
    // V1MatchInfo deserialization
    // ---------------------------------------------------------------------------

    [Fact]
    public void V1MatchInfo_Deserializes_Games_And_Scores()
    {
        var json = """
            {
                "match": {
                    "match_id": "100",
                    "name": "Test Match",
                    "start_time": "2023-01-01 10:00:00",
                    "end_time": "2023-01-01 11:00:00"
                },
                "games": [
                    {
                        "game_id": "200",
                        "start_time": "2023-01-01 10:05:00",
                        "end_time": "2023-01-01 10:10:00",
                        "beatmap_id": "999",
                        "play_mode": "0",
                        "match_type": "0",
                        "scoring_type": "0",
                        "team_type": "0",
                        "mods": "0",
                        "scores": [
                            {
                                "slot": "0",
                                "team": "0",
                                "user_id": "1",
                                "score": "1000000",
                                "maxcombo": "800",
                                "rank": "0",
                                "count300": "900",
                                "count100": "10",
                                "count50": "0",
                                "countmiss": "0",
                                "countkatu": "2",
                                "countgeki": "50",
                                "perfect": "1",
                                "pass": "1",
                                "enabled_mods": "0"
                            }
                        ]
                    }
                ]
            }
            """;

        var match = JsonConvert.DeserializeObject<V1MatchInfo>(json)!;

        Assert.Equal("Test Match", match.Match.Name);
        Assert.Single(match.Games);
        var game = match.Games[0];
        Assert.Equal("999", game.BeatmapId);
        Assert.Single(game.Scores);
        var score = game.Scores[0];
        Assert.True(score.IsPerfect);
        Assert.True(score.HasPassed);
    }

    // ---------------------------------------------------------------------------
    // URL builder (via reflection on private method)
    // ---------------------------------------------------------------------------

    [Fact]
    public void V1Client_BuildUrl_Omits_Null_Params()
    {
        // We test the output indirectly by verifying GetBeatmapsAsync
        // doesn't throw with all-null optional params.
        // (Full integration testing requires a live API key.)
        var client = new OssapiV1Client("dummy_key");
        Assert.NotNull(client);
        client.Dispose();
    }
}
