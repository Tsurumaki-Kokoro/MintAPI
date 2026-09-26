using System.Reflection;
using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Models;
using Xunit;

namespace Ossapi.Tests.Unit;

/// <summary>
/// Deserializes JSON fixture files (embedded resources) against real model classes
/// using the same JsonSerializerSettings as the live client.
/// </summary>
public class FixtureDeserializationTests
{
    private static readonly JsonSerializerSettings Settings = OssapiClient.BuildJsonSettings();

    private static string LoadFixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static T Deserialize<T>(string fixture) =>
        JsonConvert.DeserializeObject<T>(LoadFixture(fixture), Settings)!;

    // -------------------------------------------------------------------------
    // Beatmap
    // -------------------------------------------------------------------------

    [Fact]
    public void Beatmap_Deserializes_CoreFields()
    {
        var beatmap = Deserialize<Beatmap>("beatmap.json");

        Assert.Equal(221777, beatmap.Id);
        Assert.Equal(79498, beatmap.BeatmapsetId);
        Assert.Equal(1047883, beatmap.UserId);
        Assert.Equal(GameMode.Osu, beatmap.Mode);
        Assert.Equal(RankStatus.Ranked, beatmap.Status);
        Assert.Equal("Insane", beatmap.Version);
        Assert.Equal(228, beatmap.TotalLength);
        Assert.Equal(2000, beatmap.MaxCombo);
        Assert.Equal("abc123def456", beatmap.Checksum);
    }

    [Fact]
    public void Beatmap_Deserializes_ExtendedFields()
    {
        var beatmap = Deserialize<Beatmap>("beatmap.json");

        Assert.Equal(8.3, beatmap.Accuracy, precision: 5);
        Assert.Equal(9.0, beatmap.Ar, precision: 5);
        Assert.Equal(200.0, beatmap.Bpm);
        Assert.Equal(4.0, beatmap.Cs, precision: 5);
        Assert.Equal(845, beatmap.CountCircles);
        Assert.Equal(386, beatmap.CountSliders);
        Assert.Equal(2, beatmap.CountSpinners);
        Assert.Equal(100000, beatmap.Passcount);
        Assert.Equal(1500000, beatmap.Playcount);
    }

    [Fact]
    public void Beatmap_Deserializes_NestedBeatmapset()
    {
        var beatmap = Deserialize<Beatmap>("beatmap.json");

        Assert.NotNull(beatmap.Beatmapset);
        Assert.Equal(79498, beatmap.Beatmapset.Id);
        Assert.Equal("Responsibility of Pain", beatmap.Beatmapset.Title);
        Assert.Equal("Yuna", beatmap.Beatmapset.Artist);
        Assert.NotNull(beatmap.Beatmapset.Covers);
        Assert.False(string.IsNullOrEmpty(beatmap.Beatmapset.Covers.Cover));
    }

    [Fact]
    public void Beatmap_Deserializes_Failtimes()
    {
        var beatmap = Deserialize<Beatmap>("beatmap.json");

        Assert.NotNull(beatmap.Failtimes);
        Assert.Equal(3, beatmap.Failtimes.Exit!.Count);
        Assert.Equal(3, beatmap.Failtimes.Fail!.Count);
        Assert.Equal(2, beatmap.Failtimes.Exit[2]);
    }

    // -------------------------------------------------------------------------
    // User
    // -------------------------------------------------------------------------

    [Fact]
    public void User_Deserializes_CoreCompactFields()
    {
        var user = Deserialize<User>("user.json");

        Assert.Equal(12092800, user.Id);
        Assert.Equal("TestPlayer", user.Username);
        Assert.Equal("US", user.CountryCode);
        Assert.True(user.IsSupporter);
        Assert.False(user.IsBot);
        Assert.NotNull(user.LastVisit);
        Assert.Equal(2024, user.LastVisit!.Value.Year);
        Assert.Equal("#FF5733", user.ProfileColour);
    }

    [Fact]
    public void User_Deserializes_ExtendedUserFields()
    {
        var user = Deserialize<User>("user.json");

        Assert.Equal("osu", user.Playmode);
        Assert.Equal("USA", user.Location);
        Assert.Equal(42, user.CommentsCount);
        Assert.Equal(100, user.PostCount);
        Assert.Equal("@testplayer", user.Twitter);
        Assert.True(user.HasSupported);
        Assert.Equal(2018, user.JoinDate.Year);
    }

    [Fact]
    public void User_Deserializes_Statistics()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.Statistics);
        Assert.Equal(7500.25, user.Statistics!.Pp!.Value, precision: 5);
        Assert.Equal(98.75, user.Statistics.HitAccuracy, precision: 5);
        Assert.Equal(15000, user.Statistics.GlobalRank);
        Assert.Equal(500, user.Statistics.CountryRank);
        Assert.True(user.Statistics.IsRanked);
        Assert.Equal(25000, user.Statistics.PlayCount);
        Assert.Equal(98.75, user.Statistics.Accuracy, precision: 5);
        Assert.Equal(-50, user.Statistics.RankChangeSince30Days);
    }

    [Fact]
    public void User_Deserializes_GradeCounts()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.Statistics?.GradeCounts);
        Assert.Equal(100, user.Statistics!.GradeCounts!.Ss);
        Assert.Equal(50, user.Statistics.GradeCounts.Ssh);
        Assert.Equal(500, user.Statistics.GradeCounts.S);
        Assert.Equal(1000, user.Statistics.GradeCounts.A);
    }

    [Fact]
    public void User_Deserializes_Badges()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.Badges);
        Assert.Single(user.Badges!);
        Assert.Equal("3 Digit", user.Badges[0].Description);
        Assert.Equal(2020, user.Badges[0].AwardedAt.Year);
    }

    [Fact]
    public void User_Deserializes_Country()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.Country);
        Assert.Equal("US", user.Country!.Code);
        Assert.Equal("United States", user.Country.Name);
    }

    [Fact]
    public void User_Deserializes_RankHistory()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.RankHistory);
        Assert.Equal(6, user.RankHistory!.Data.Count);
        Assert.Equal(15000, user.RankHistory.Data[^1]);
    }

    [Fact]
    public void User_Deserializes_UserAchievements()
    {
        var user = Deserialize<User>("user.json");

        Assert.NotNull(user.UserAchievements);
        Assert.Equal(2, user.UserAchievements!.Count);
        Assert.Equal(1, user.UserAchievements[0].AchievementId);
    }

    [Fact]
    public void User_Deserializes_ProfileOrder()
    {
        var user = Deserialize<User>("user.json");

        Assert.Equal(6, user.ProfileOrder.Count);
        Assert.Equal("me", user.ProfileOrder[0]);
    }

    // -------------------------------------------------------------------------
    // Score
    // -------------------------------------------------------------------------

    [Fact]
    public void Score_Deserializes_CoreFields()
    {
        var score = Deserialize<Score>("score.json");

        Assert.Equal(368525533, score.Id);
        Assert.Equal(12092800, score.UserId);
        Assert.Equal(Grade.S, score.Rank);
        Assert.Equal(0.9875, score.Accuracy, precision: 5);
        Assert.Equal(1500, score.MaxCombo);
        Assert.Equal(450.25, score.Pp!.Value, precision: 5);
        Assert.True(score.Passed);
        Assert.True(score.Replay);
        Assert.Equal(221777, score.BeatmapId);
        Assert.Equal("solo_score", score.Type);
        Assert.Equal(10, score.RankCountry);
        Assert.Equal(500, score.RankGlobal);
        Assert.Equal(2023, score.EndedAt.Year);
        Assert.NotNull(score.StartedAt);
    }

    [Fact]
    public void Score_Deserializes_Mods()
    {
        var score = Deserialize<Score>("score.json");

        Assert.NotNull(score.Mods);
        Assert.Equal(2, score.Mods!.Count);
        Assert.Equal("HD", score.Mods[0].Acronym);
        Assert.Equal("HR", score.Mods[1].Acronym);
    }

    [Fact]
    public void Score_Deserializes_Statistics()
    {
        var score = Deserialize<Score>("score.json");

        Assert.NotNull(score.Statistics);
        Assert.Equal(2, score.Statistics!.Miss);
        Assert.Equal(5, score.Statistics.Meh);
        Assert.Equal(800, score.Statistics.Great);
    }

    [Fact]
    public void Score_Deserializes_NestedBeatmap()
    {
        var score = Deserialize<Score>("score.json");

        Assert.NotNull(score.Beatmap);
        Assert.Equal(221777, score.Beatmap!.Id);
        Assert.Equal(GameMode.Osu, score.Beatmap.Mode);
        Assert.Equal("Insane", score.Beatmap.Version);
    }

    [Fact]
    public void Score_Deserializes_Weight()
    {
        var score = Deserialize<Score>("score.json");

        Assert.NotNull(score.Weight);
        Assert.Equal(100.0, score.Weight!.Percentage, precision: 5);
        Assert.Equal(450.25, score.Weight.Pp, precision: 5);
    }

    [Fact]
    public void Score_Deserializes_NestedUser()
    {
        var score = Deserialize<Score>("score.json");

        Assert.NotNull(score.User);
        Assert.Equal(12092800, score.User!.Id);
        Assert.Equal("TestPlayer", score.User.Username);
        Assert.Equal("US", score.User.CountryCode);
    }

    // -------------------------------------------------------------------------
    // Rankings
    // -------------------------------------------------------------------------

    [Fact]
    public void Rankings_Deserializes_TopLevel()
    {
        var rankings = Deserialize<Rankings>("rankings.json");

        Assert.Equal(10000, rankings.Total);
        Assert.Equal(2, rankings.Ranking.Count);
        Assert.NotNull(rankings.Cursor);
        Assert.Equal("eyJwYWdlIjoyfQ==", rankings.CursorString);
    }

    [Fact]
    public void Rankings_Deserializes_UserStatistics()
    {
        var rankings = Deserialize<Rankings>("rankings.json");

        var top = rankings.Ranking[0];
        Assert.Equal(1, top.GlobalRank);
        Assert.Equal(16500.0, top.Pp!.Value, precision: 5);
        Assert.Equal(99.5, top.HitAccuracy, precision: 5);
        Assert.Equal(50000, top.PlayCount);
        Assert.True(top.IsRanked);
        Assert.Equal(1000, top.GradeCounts!.Ss);
    }

    [Fact]
    public void Rankings_Deserializes_EmbeddedUser()
    {
        var rankings = Deserialize<Rankings>("rankings.json");

        var top = rankings.Ranking[0];
        Assert.NotNull(top.User);
        Assert.Equal(1, top.User!.Id);
        Assert.Equal("TopPlayer", top.User.Username);
        Assert.Equal("KR", top.User.CountryCode);

        var second = rankings.Ranking[1];
        Assert.NotNull(second.User);
        Assert.Equal(2, second.User!.Id);
        Assert.Equal("JP", second.User.CountryCode);
    }

    // -------------------------------------------------------------------------
    // User scores list
    // -------------------------------------------------------------------------

    [Fact]
    public void UserScores_Deserializes_List()
    {
        var scores = Deserialize<List<Score>>("user_scores.json");

        Assert.Equal(2, scores.Count);

        var first = scores[0];
        Assert.Equal(400000001, first.Id);
        Assert.Equal(Grade.SS, first.Rank);
        Assert.True(first.IsPerfectCombo);
        Assert.True(first.LegacyPerfect);
        Assert.Null(first.StartedAt);
        Assert.Equal(2, first.Mods!.Count);
        Assert.Equal("HD", first.Mods[0].Acronym);
        Assert.Equal("DT", first.Mods[1].Acronym);
        Assert.Equal(300000, first.Beatmap!.Id);

        var second = scores[1];
        Assert.Equal(400000002, second.Id);
        Assert.Equal(Grade.A, second.Rank);
        Assert.Empty(second.Mods!);
        Assert.Equal(987654321, second.LegacyScoreId);
        Assert.NotNull(second.StartedAt);
        Assert.Equal(90.51, second.Weight!.Percentage, precision: 2);
    }
}
