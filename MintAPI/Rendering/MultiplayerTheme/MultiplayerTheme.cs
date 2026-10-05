using System.Globalization;
using System.Net;
using MintAPI.Services;
using Scriban;
using Scriban.Runtime;

namespace MintAPI.Rendering.MultiplayerTheme;

public sealed class MultiplayerTheme(IRenderService renderer, IImageCacheService imageCache,
    ILogger<MultiplayerTheme> logger, IBeatmapFileService? beatmapFiles = null)
{
    public async Task<byte[]> RenderHistoryAsync(MultiplayerData data, int page, CancellationToken cancellationToken = default)
    {
        var pages = data.HistoryPages();
        if (page < 0 || page > pages.Count) throw new ArgumentException($"page 必须为 0 或在 1 至 {pages.Count} 之间。");
        var rounds = page == 0 ? data.Rounds : pages[page - 1];
        var avatars = await AvatarsAsync(rounds.SelectMany(round => round.Players)
            .Select(player => (player.UserId, player.Avatar)));
        var covers = new Dictionary<int, string>();
        foreach (var round in rounds)
        {
            var setId = round.Source.Beatmap?.Beatmapset?.Id ?? round.Source.Beatmap?.BeatmapsetId ?? 0;
            if (!covers.ContainsKey(setId)) covers[setId] = await CoverAsync(setId, cancellationToken);
        }
        var values = Common(data, page, page == 0 ? 1 : pages.Count);
        values["rounds"] = rounds.Select(round => new
        {
            index = round.Index, mode = round.Mode,
            cover = covers[round.Source.Beatmap?.Beatmapset?.Id ?? round.Source.Beatmap?.BeatmapsetId ?? 0],
            title = Escape(round.Source.Beatmap?.Beatmapset?.Title ?? $"Beatmap {round.Source.BeatmapId}"),
            artist = Escape(round.Source.Beatmap?.Beatmapset?.Artist ?? ""),
            difficulty = Escape(round.Source.Beatmap?.Version ?? "未知难度"),
            stars_color = round.Source.Beatmap is null ? "#65656e" : MintAPI.Rendering.BeatmapTheme.DefaultBeatmapTheme.GetStarsColor(round.Source.Beatmap.DifficultyRating),
            stars = round.Source.Beatmap?.DifficultyRating.ToString("0.00", CultureInfo.InvariantCulture) ?? "—",
            map_id = round.Source.BeatmapId,
            scoring = round.Source.ScoringType switch { MintOsuApi.Enums.ScoringType.Accuracy => "准确率计分", MintOsuApi.Enums.ScoringType.Combo => "连击计分", MintOsuApi.Enums.ScoringType.ScoreV2 => "Score V2", _ => "Score" },
            result = round.IsTeam ? round.Winner switch { "red" => Escape(data.RedName) + " 获胜", "blue" => Escape(data.BlueName) + " 获胜", _ => "平局" }
                : round.Source.TeamType == MintOsuApi.Enums.TeamType.TagCoop ? "合作对局" : round.Players.Count(player => MultiplayerData.Metric(round.Source, player) == MultiplayerData.Metric(round.Source, round.Players[0])) > 1
                    ? "并列第一" : Escape(round.Players[0].Name) + " 第一",
            winner = round.IsTeam ? round.Winner : "none",
            red_score = Number(round.RedScore), blue_score = Number(round.BlueScore), is_team = round.IsTeam,
            red_leads = round.RedScore > round.BlueScore, blue_leads = round.BlueScore > round.RedScore,
            players = round.Players.Select((player, rank) => new
            {
                rank = rank + 1, name = Escape(player.Name), team = TeamClass(player.Team),
                avatar = avatars[player.UserId], initial = Escape(Initial(player.Name)),
                score = Number(player.Score), accuracy = (player.Accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture),
                combo = Number(player.Combo), mods = Escape(player.Mods), passed = player.Passed
            }).ToArray()
        }).ToArray();
        return await RenderAsync("history", values, 0, cancellationToken);
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
            team_name = player.Team switch { "red" => "红队", "blue" => "蓝队", "mixed" => "换队", _ => "个人" },
            avatar = avatars[player.UserId], initial = Escape(Initial(player.Name)),
            rating = player.Rating.ToString("0.00", CultureInfo.InvariantCulture), total = Number(player.TotalScore),
            average = Number(player.AverageScore), played = player.Played,
            record = data.Rounds[0].IsTeam ? $"{player.Wins}W · {player.Losses}L · {player.Draws}D" : $"{player.Wins} 次第一",
            rate = (100.0 * player.Wins / player.Played).ToString("0.0", CultureInfo.InvariantCulture),
            bar = (100 * player.Rating / players[0].Rating).ToString("0.0", CultureInfo.InvariantCulture)
        }).ToArray();
        return await RenderAsync("rating", values, 0, cancellationToken);
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

    private async Task<string> CoverAsync(int setId, CancellationToken cancellationToken)
    {
        if (beatmapFiles is null || setId <= 0) return "";
        try
        {
            var bytes = await beatmapFiles.GetListCoverAsync(setId, cancellationToken);
            return bytes is { Length: > 0 } ? $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}" : "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load multiplayer cover for {SetId}", setId);
            return "";
        }
    }

    private async Task<Dictionary<int, string>> AvatarsAsync(IEnumerable<(int UserId, string Avatar)> players)
    {
        var result = new Dictionary<int, string>();
        foreach (var player in players.DistinctBy(player => player.UserId))
        {
            try
            {
                var bytes = await imageCache.GetAvatarAsync(player.Avatar, player.UserId);
                result[player.UserId] = bytes.Length == 0 ? "" : $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
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
        return await renderer.RenderHtmlAsync(await template.RenderAsync(context), 1500, height, cancellationToken);
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
    private static string Number(double value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Initial(string value) => string.IsNullOrEmpty(value) ? "?" : StringInfo.GetNextTextElement(value);
    private static string TeamClass(string value) => value is "red" or "blue" or "mixed" ? value : "none";
}
