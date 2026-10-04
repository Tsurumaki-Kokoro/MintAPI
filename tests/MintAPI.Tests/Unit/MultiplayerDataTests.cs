using MintAPI.Rendering.MultiplayerTheme;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public class MultiplayerDataTests
{
    [Fact]
    public async Task Current_match_109850222_has_correct_scores_standings_and_ratings()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "match-109850222.json"));
        var match = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchResponse>(json, MintOsuApi.OsuClient.BuildJsonSettings())!;
        var data = MultiplayerData.Build(match);
        Assert.Equal(7, data.Rounds.Count);
        Assert.Equal(7, data.PlayerCount);
        Assert.Equal(2, data.RedWins);
        Assert.Equal(5, data.BlueWins);
        Assert.Equal(2, data.HistoryPages().Count);
        foreach (var algorithm in new[] { "osuplus", "bathbot", "flashlight" })
        {
            var ratings = data.Rate(algorithm);
            Assert.Equal(7, ratings.Count);
            Assert.All(ratings, player => Assert.True(double.IsFinite(player.Rating) && player.Rating > 0));
            Assert.Equal(5409989L, ratings[0].TotalScore);
            Assert.Equal(7, ratings[0].Played);
        }
        Assert.Equal(2.39, Math.Round(data.Rate("osuplus")[0].Rating, 2));
    }

    [Theory]
    [InlineData("osuplus", 2.0 / 3)]
    [InlineData("bathbot", 1.5)]
    [InlineData("flashlight", 1)]
    public void Single_equal_score_game_has_finite_rating(string algorithm, double expected)
    {
        var data = MultiplayerData.Build(Sample());
        Assert.All(data.Rate(algorithm), player => Assert.Equal(expected, player.Rating, 8));
    }

    [Fact]
    public void Ties_are_draws_and_zero_scores_remain_in_history_only()
    {
        var match = Sample();
        match.EventList[0].Game!.Scores.Add(new LegacyScore { UserId = 3, Score = 0 });
        var data = MultiplayerData.Build(match);
        Assert.Equal(3, data.Rounds[0].Players.Count);
        Assert.Equal(0, data.RedWins);
        Assert.Equal(0, data.BlueWins);
        Assert.All(data.Rate("osuplus"), player => { Assert.Equal(1, player.Draws); Assert.Equal(0, player.Losses); });
        Assert.Equal(2, data.Rate("osuplus").Count);
    }

    [Fact]
    public void Individual_tied_first_place_counts_for_every_tied_player()
    {
        var data = MultiplayerData.Build(Sample(false));
        Assert.All(data.Rate("osuplus"), player => Assert.Equal(1, player.Wins));
    }

    [Fact]
    public void History_pages_keep_games_whole_and_skip_unfinished_games()
    {
        var match = Sample(rounds: 20);
        match.EventList[^1].Game!.EndTime = null;
        var data = MultiplayerData.Build(match);
        var pages = data.HistoryPages();
        Assert.Equal(19, pages.Sum(page => page.Count));
        Assert.Equal(2, pages.Count);
        Assert.All(pages, page => Assert.True(page.Sum(round => round.Players.Count) <= 32));
    }

    [Fact]
    public void Mixed_modes_require_rating_filter()
    {
        var match = Sample(rounds: 2);
        match.EventList[1].Game!.TeamType = TeamType.HeadToHead;
        Assert.Throws<ArgumentException>(() => MultiplayerData.Build(match).Rate("osuplus"));
        Assert.Single(MultiplayerData.Build(match, "team-vs").Rounds);
    }

    [Fact]
    public void Accuracy_history_orders_by_accuracy_and_rating_rejects_it()
    {
        var match = Sample();
        match.EventList[0].Game!.ScoringType = ScoringType.Accuracy;
        match.EventList[0].Game!.Scores[1].Accuracy = 1;
        var data = MultiplayerData.Build(match);
        Assert.Equal(2, data.Rounds[0].Players[0].UserId);
        Assert.Equal("blue", data.Rounds[0].Winner);
        Assert.Throws<ArgumentException>(() => data.Rate("osuplus"));
    }

    [Fact]
    public void Bathbot_uses_final_game_for_tiebreak_bonus()
    {
        var match = Sample(rounds: 3);
        match.EventList[0].Game!.Scores[0].Score = 300;
        match.EventList[1].Game!.Scores[1].Score = 300;
        match.EventList[2].Game!.Scores[0].Score = 300;
        var data = MultiplayerData.Build(match);
        var red = data.Rate("bathbot").Single(player => player.UserId == 1);
        Assert.Equal((1.5 + .5 + 1.5 + 1.5 + 1.5) / 3 * Math.Pow(1.4, .6), red.Rating, 8);
    }

    [Fact]
    public void Empty_or_zero_only_rating_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => MultiplayerData.Build(new MatchResponse()));
        var match = Sample();
        foreach (var score in match.EventList[0].Game!.Scores) score.Score = 0;
        Assert.Throws<ArgumentException>(() => MultiplayerData.Build(match).Rate("flashlight"));
    }

    internal static MatchResponse Sample(bool team = true, int rounds = 1)
    {
        var start = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        return new MatchResponse
        {
            FirstEventId = 1, LatestEventId = rounds,
            MatchInfo = new Match { Id = 12345, Name = "Autumn Invitational: (Aster) vs (Orbit)", StartTime = start, EndTime = start.AddHours(1) },
            Users = [new UserCompact { Id = 1, Username = "Aster", AvatarUrl = "https://a.ppy.sh/1" },
                new UserCompact { Id = 2, Username = "Orbit", AvatarUrl = "https://a.ppy.sh/2" }],
            EventList = Enumerable.Range(1, rounds).Select(index => new MatchEvent
            {
                Id = index, Game = new MatchGame
                {
                    Id = index, BeatmapId = 123 + index, StartTime = start.AddMinutes(index * 3), EndTime = start.AddMinutes(index * 3 + 2),
                    TeamType = team ? TeamType.TeamVs : TeamType.HeadToHead, ScoringType = ScoringType.ScoreV2,
                    Beatmap = new BeatmapCompact { Id = 123 + index, Version = "Another", DifficultyRating = 6.42,
                        Beatmapset = new Beatmapset { Title = index % 2 == 0 ? "星の海 / Sea of Stars" : "A New Beginning", Artist = "Sample Artist" } },
                    Scores = [new LegacyScore { UserId = 1, Score = 100, Accuracy = .98, MaxCombo = 800, Match = new ScoreMatchInfo { Team = team ? "red" : "none", Pass = true } },
                        new LegacyScore { UserId = 2, Score = 100, Accuracy = .97, MaxCombo = 700, Match = new ScoreMatchInfo { Team = team ? "blue" : "none", Pass = true } }]
                }
            }).ToList()
        };
    }
}
