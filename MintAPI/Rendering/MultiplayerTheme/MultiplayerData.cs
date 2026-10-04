using System.Globalization;
using System.Text.RegularExpressions;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Rendering.MultiplayerTheme;

public sealed record MatchPlayer(int UserId, string Name, string Avatar, string Team, long Score,
    double Accuracy, int Combo, string Mods, bool Passed);
public sealed record MatchRound(int Index, MatchGame Source, IReadOnlyList<MatchPlayer> Players,
    string Winner, long RedScore, long BlueScore)
{
    public bool IsTeam => Source.TeamType is TeamType.TeamVs or TeamType.TagTeamVs;
    public string Mode => Source.TeamType switch
    {
        TeamType.TeamVs => "团队对抗", TeamType.TagTeamVs => "接力团队", TeamType.TagCoop => "接力合作", _ => "个人对抗"
    };
}
public sealed record RatedPlayer(int UserId, string Name, string Avatar, string Team, double Rating,
    long TotalScore, double AverageScore, int Played, int Wins, int Losses, int Draws);

public sealed class MultiplayerData
{
    public int MatchId { get; init; }
    public string Title { get; init; } = "";
    public string RedName { get; init; } = "红队";
    public string BlueName { get; init; } = "蓝队";
    public string TimeRange { get; init; } = "";
    public bool Complete { get; init; }
    public IReadOnlyList<MatchRound> Rounds { get; init; } = [];
    public int RedWins => Rounds.Count(round => round.IsTeam && round.Winner == "red");
    public int BlueWins => Rounds.Count(round => round.IsTeam && round.Winner == "blue");
    public int PlayerCount => Rounds.SelectMany(round => round.Players).Select(player => player.UserId).Distinct().Count();

    public static MultiplayerData Build(MatchResponse match, string? teamType = null)
    {
        var users = match.Users.ToDictionary(user => user.Id);
        var games = match.EventList.Where(item => item.Game?.EndTime != null).Select(item => item.Game!)
            .DistinctBy(game => game.Id).OrderBy(game => game.StartTime).ThenBy(game => game.Id);
        var rounds = new List<MatchRound>();
        foreach (var game in games)
        {
            if (teamType != null && game.TeamType != ParseTeamType(teamType)) continue;
            var players = game.Scores.Select(score =>
            {
                users.TryGetValue(score.UserId, out var user);
                var mods = (game.Mods | score.Mods).ToShortName();
                return new MatchPlayer(score.UserId, user?.Username ?? score.UserId.ToString(),
                    user?.AvatarUrl ?? $"https://a.ppy.sh/{score.UserId}", score.Match?.Team ?? "none",
                    score.Score, score.Accuracy, score.MaxCombo, string.IsNullOrEmpty(mods) ? "NM" : mods,
                    score.Match?.Pass ?? score.Passed);
            }).OrderByDescending(player => Metric(game, player)).ThenBy(player => player.UserId).ToArray();
            if (players.Length == 0) continue;
            var red = players.Where(player => player.Team == "red").Sum(player => player.Score);
            var blue = players.Where(player => player.Team == "blue").Sum(player => player.Score);
            var redMetric = players.Where(player => player.Team == "red").Sum(player => Metric(game, player));
            var blueMetric = players.Where(player => player.Team == "blue").Sum(player => Metric(game, player));
            rounds.Add(new(rounds.Count + 1, game, players,
                redMetric > blueMetric ? "red" : blueMetric > redMetric ? "blue" : "draw", red, blue));
        }
        if (rounds.Count == 0) throw new ArgumentException("该多人房没有已结束且包含成绩的对局。");
        var names = Regex.Match(match.MatchInfo.Name, @"^(.+?):\s*[（(](.+?)[）)]\s+vs\s+[（(](.+?)[）)]", RegexOptions.IgnoreCase);
        var start = match.MatchInfo.StartTime.ToOffset(TimeSpan.FromHours(8));
        var end = match.MatchInfo.EndTime?.ToOffset(TimeSpan.FromHours(8));
        return new MultiplayerData
        {
            MatchId = match.MatchInfo.Id, Title = names.Success ? names.Groups[1].Value : match.MatchInfo.Name,
            RedName = names.Success ? names.Groups[2].Value : "红队", BlueName = names.Success ? names.Groups[3].Value : "蓝队",
            TimeRange = $"{start:yyyy/MM/dd HH:mm} — {(end.HasValue ? end.Value.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) : "进行中")} · UTC+8",
            Complete = end.HasValue, Rounds = rounds
        };
    }

    public static TeamType ParseTeamType(string value) => value switch
    {
        "team-vs" => TeamType.TeamVs, "head-to-head" => TeamType.HeadToHead,
        "tag-team-vs" => TeamType.TagTeamVs, "tag-coop" => TeamType.TagCoop,
        _ => throw new ArgumentException("team_type 必须为 team-vs、head-to-head、tag-team-vs 或 tag-coop。")
    };

    public static double Metric(MatchGame game, MatchPlayer player) => game.ScoringType switch
    {
        ScoringType.Accuracy => player.Accuracy, ScoringType.Combo => player.Combo, _ => player.Score
    };

    public IReadOnlyList<RatedPlayer> Rate(string algorithm)
    {
        if (algorithm is not ("osuplus" or "bathbot" or "flashlight"))
            throw new ArgumentException("algorithm 必须为 osuplus、bathbot 或 flashlight。");
        if (Rounds.Select(round => round.Source.TeamType).Distinct().Count() > 1)
            throw new ArgumentException("该房间包含多种模式，请使用 team_type 选择评分模式。");
        if (Rounds.Any(round => round.Source.TeamType is TeamType.TagCoop or TeamType.TagTeamVs ||
                                round.Source.ScoringType is ScoringType.Accuracy or ScoringType.Combo))
            throw new ArgumentException("评分仅支持按分数计分的个人对抗或团队对抗。");
        // Zero scores remain in history, but do not count as rating participation.
        var active = Rounds.Select(round => (Round: round, Players: round.Players.Where(player => player.Score > 0).ToArray()))
            .Where(item => item.Players.Length > 0).ToArray();
        if (active.Length == 0) throw new ArgumentException("该多人房没有可用于评分的非零成绩。");
        var appearances = active.SelectMany(item => item.Players).GroupBy(player => player.UserId)
            .ToDictionary(group => group.Key, group => group.Count());
        var medianAppearances = Median(appearances.Values.Select(value => (double)value));
        var redWins = 0;
        var blueWins = 0;
        for (var i = 0; i < active.Length - 1; i++)
        {
            if (active[i].Round.Winner == "red") redWins++;
            if (active[i].Round.Winner == "blue") blueWins++;
        }
        var tiebreaker = active.Length > 1 && active[^1].Round.IsTeam && redWins == blueWins && redWins > 0;
        var results = new List<RatedPlayer>();
        foreach (var id in appearances.Keys)
        {
            var played = active.Where(item => item.Players.Any(player => player.UserId == id)).ToArray();
            var entries = played.Select(item => item.Players.Single(player => player.UserId == id)).ToArray();
            var ratios = played.Sum(item => item.Players.Single(player => player.UserId == id).Score / item.Players.Average(player => player.Score));
            var n = entries.Length;
            var bonus = tiebreaker ? (active[^1].Players.FirstOrDefault(player => player.UserId == id)?.Score ?? 0) /
                active[^1].Players.Average(player => player.Score) : 0;
            var mods = played.SelectMany(item =>
                (item.Round.Source.Mods | item.Round.Source.Scores.Single(score => score.UserId == id).Mods)
                    .Decompose(true).Select(mod => mod.ToShortName()))
                .Where(mod => mod != "NM").Distinct().Count();
            var rating = algorithm switch
            {
                "osuplus" => 2.0 / (n + 2) * ratios,
                "bathbot" => (ratios + n * .5 + bonus) / n *
                    Math.Pow(1.4, .6 * (n - 1) / Math.Max(1, active.Length - 1)) * (1 + .02 * Math.Max(0, mods - 2)),
                _ => played.Sum(item => item.Players.Single(player => player.UserId == id).Score /
                    Median(item.Players.Select(player => (double)player.Score))) / n * Math.Cbrt(n / medianAppearances)
            };
            var wins = 0;
            var draws = 0;
            foreach (var item in played)
            {
                var player = item.Players.Single(player => player.UserId == id);
                if (item.Round.IsTeam)
                {
                    if (item.Round.Winner == "draw") draws++;
                    else if (player.Team == item.Round.Winner) wins++;
                }
                else if (player.Score == item.Players.Max(other => other.Score)) wins++;
            }
            var teams = entries.Select(player => player.Team).Distinct().ToArray();
            results.Add(new(id, entries[0].Name, entries[0].Avatar, teams.Length == 1 ? teams[0] : "mixed", rating,
                entries.Sum(player => player.Score), entries.Average(player => player.Score), n, wins, n - wins - draws, draws));
        }
        return results.OrderByDescending(player => player.Rating).ThenBy(player => player.UserId).ToArray();
    }

    private static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        return (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
    }

    public IReadOnlyList<IReadOnlyList<MatchRound>> HistoryPages(int rowsPerPage = 32)
    {
        var pages = new List<IReadOnlyList<MatchRound>>();
        var current = new List<MatchRound>();
        var rows = 0;
        foreach (var round in Rounds)
        {
            if (current.Count > 0 && rows + round.Players.Count > rowsPerPage)
            {
                pages.Add(current); current = []; rows = 0;
            }
            current.Add(round); rows += round.Players.Count;
        }
        if (current.Count > 0) pages.Add(current);
        return pages;
    }
}
