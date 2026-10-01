using System.Globalization;
using System.Net;
using HitCircleAPI.Services;
using Scriban;
using Scriban.Runtime;

namespace HitCircleAPI.Rendering.MultiplayerTheme;

public sealed class MultiplayerTheme(IRenderService renderer, IImageCacheService imageCache,
    ILogger<MultiplayerTheme> logger)
{
    public async Task<byte[]> RenderHistoryAsync(MultiplayerData data, int page, CancellationToken cancellationToken = default)
    {
        var pages = data.HistoryPages();
        if (page < 1 || page > pages.Count) throw new ArgumentException($"page 必须在 1 至 {pages.Count} 之间。");
        var rounds = pages[page - 1];
        var avatars = await AvatarsAsync(rounds.SelectMany(round => round.Players)
            .Select(player => (player.UserId, player.Avatar)));
        var values = Common(data, page, pages.Count);
        values["rounds"] = rounds.Select(round => new
        {
            index = round.Index, mode = round.Mode,
            title = Escape(round.Source.Beatmap?.Beatmapset?.Title ?? $"Beatmap {round.Source.BeatmapId}"),
            artist = Escape(round.Source.Beatmap?.Beatmapset?.Artist ?? ""),
            difficulty = Escape(round.Source.Beatmap?.Version ?? "未知难度"),
            stars = round.Source.Beatmap?.DifficultyRating.ToString("0.00", CultureInfo.InvariantCulture) ?? "—",
            map_id = round.Source.BeatmapId,
            scoring = round.Source.ScoringType switch { Ossapi.Enums.ScoringType.Accuracy => "准确率计分", Ossapi.Enums.ScoringType.Combo => "连击计分", Ossapi.Enums.ScoringType.ScoreV2 => "Score V2", _ => "Score" },
            result = round.IsTeam ? round.Winner switch { "red" => Escape(data.RedName) + " 获胜", "blue" => Escape(data.BlueName) + " 获胜", _ => "平局" }
                : round.Source.TeamType == Ossapi.Enums.TeamType.TagCoop ? "合作对局" : round.Players.Count(player => MultiplayerData.Metric(round.Source, player) == MultiplayerData.Metric(round.Source, round.Players[0])) > 1
                    ? "并列第一" : Escape(round.Players[0].Name) + " 第一",
            winner = round.IsTeam ? round.Winner : "none",
            red_score = Number(round.RedScore), blue_score = Number(round.BlueScore), is_team = round.IsTeam,
            players = round.Players.Select((player, rank) => new
            {
                rank = rank + 1, name = Escape(player.Name), team = TeamClass(player.Team),
                avatar = avatars[player.UserId], initial = Escape(Initial(player.Name)),
                score = Number(player.Score), accuracy = (player.Accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture),
                combo = Number(player.Combo), mods = Escape(player.Mods), passed = player.Passed
            }).ToArray()
        }).ToArray();
        var height = 398 + rounds.Sum(round => 178 + 48 * round.Players.Count);
        return await RenderAsync("history", values, height, cancellationToken);
    }

    public async Task<byte[]> RenderRatingAsync(MultiplayerData data, string algorithm, int page,
        CancellationToken cancellationToken = default)
    {
        var players = data.Rate(algorithm);
        var pages = (players.Count + 23) / 24;
        if (page < 1 || page > pages) throw new ArgumentException($"page 必须在 1 至 {pages} 之间。");
        var shown = players.Skip((page - 1) * 24).Take(24).ToArray();
        var avatars = await AvatarsAsync(shown.Append(players[0]).Select(player => (player.UserId, player.Avatar)));
        var values = Common(data, page, pages);
        values["algorithm"] = algorithm.ToUpperInvariant();
        values["is_team"] = data.Rounds[0].IsTeam;
        values["mvp_name"] = Escape(players[0].Name);
        values["mvp_rating"] = players[0].Rating.ToString("0.00", CultureInfo.InvariantCulture);
        values["mvp_avatar"] = avatars[players[0].UserId];
        values["mvp_initial"] = Escape(Initial(players[0].Name));
        values["rating_player_count"] = players.Count;
        values["players"] = shown.Select((player, index) => new
        {
            rank = (page - 1) * 24 + index + 1, name = Escape(player.Name), team = TeamClass(player.Team),
            team_name = Escape(player.Team switch { "red" => data.RedName, "blue" => data.BlueName, "mixed" => "换队", _ => "个人" }),
            avatar = avatars[player.UserId], initial = Escape(Initial(player.Name)),
            rating = player.Rating.ToString("0.00", CultureInfo.InvariantCulture), total = Number(player.TotalScore),
            average = Number(player.AverageScore), played = player.Played,
            record = data.Rounds[0].IsTeam ? $"{player.Wins}W · {player.Losses}L · {player.Draws}D" : $"{player.Wins} 次第一",
            rate = (100.0 * player.Wins / player.Played).ToString("0.0", CultureInfo.InvariantCulture),
            bar = (100 * player.Rating / players[0].Rating).ToString("0.0", CultureInfo.InvariantCulture)
        }).ToArray();
        return await RenderAsync("rating", values, 608 + shown.Length * 72, cancellationToken);
    }

    private static ScriptObject Common(MultiplayerData data, int page, int pages) => new()
    {
        ["base_url"] = new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot") + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/'),
        ["match_id"] = data.MatchId, ["title"] = Escape(data.Title), ["time_range"] = data.TimeRange,
        ["complete"] = data.Complete, ["red_name"] = Escape(data.RedName), ["blue_name"] = Escape(data.BlueName),
        ["red_wins"] = data.RedWins, ["blue_wins"] = data.BlueWins,
        ["has_teams"] = data.Rounds.Any(round => round.IsTeam),
        ["game_count"] = data.Rounds.Count, ["player_count"] = data.PlayerCount, ["page"] = page, ["pages"] = pages
    };

    private async Task<Dictionary<int, string>> AvatarsAsync(IEnumerable<(int UserId, string Avatar)> players)
    {
        var result = new Dictionary<int, string>();
        foreach (var player in players.DistinctBy(player => player.UserId))
        {
            try
            {
                var bytes = await imageCache.GetAvatarAsync(player.Avatar, player.UserId);
                result[player.UserId] = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Avatar unavailable for multiplayer user {UserId}", player.UserId);
                result[player.UserId] = "";
            }
        }
        return result;
    }

    private async Task<byte[]> RenderAsync(string name, ScriptObject values, int height, CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rendering", "MultiplayerTheme", "templates", "default", name + ".html");
        var template = Template.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        if (template.HasErrors) throw new InvalidOperationException(string.Join("\n", template.Messages));
        var context = new TemplateContext();
        context.PushGlobal(values);
        return await renderer.RenderHtmlAsync(await template.RenderAsync(context), 1200, height, cancellationToken);
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Initial(string value) => string.IsNullOrEmpty(value) ? "?" : StringInfo.GetNextTextElement(value);
    private static string TeamClass(string value) => value is "red" or "blue" or "mixed" ? value : "none";
}
