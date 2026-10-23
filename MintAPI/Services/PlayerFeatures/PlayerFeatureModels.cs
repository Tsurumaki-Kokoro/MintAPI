using MintAPI.Models.Entities;
using MintOsuApi.Models;

namespace MintAPI.Services.PlayerFeatures;

public sealed record RankedBest(int Position, Score Score);
public sealed record WeeklyReport(User User, int GameMode, DateOnly Start, DateOnly End,
    UserOsuInfoHistory? Baseline, UserOsuInfoHistory? Final, List<UserOsuInfoHistory> Points,
    List<RankedBest> NewBestPlays, bool BestCoverage, DateTimeOffset? BestObservedAt, string TimeZone);
public sealed record ComparedScore(User User, Score Score, string Source);
public sealed record ScoreComparison(Beatmap Map, int GameMode, ComparedScore Left, ComparedScore Right, bool Comparable);
public sealed record WatchSnapshot(User User, List<Score> Scores);
public sealed record SubscriptionResult(string Id, int UserId, int GameMode, long Cursor, DateTimeOffset ExpiresAt);
public sealed record WatchUpdate(long Cursor, DateTimeOffset ObservedAt, string Text);
public sealed record WatchUpdates(long Cursor, DateTimeOffset CheckedAt, DateTimeOffset ExpiresAt, bool HasMore, List<WatchUpdate> Updates);

public sealed class PlayerWatchOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 300;
    public int SubscriptionDays { get; set; } = 7;
    public int MaxTrackedUsers { get; set; } = 100;
    public bool IsValid() => PollSeconds is >= 60 and <= 86400 && SubscriptionDays is >= 1 and <= 90
        && MaxTrackedUsers is >= 1 and <= 1000;
}

/// <summary>群身份映射到既有 scope，兼容 platform:group:groupId 格式的订阅。</summary>
public sealed record PlayerWatchGroup(string Platform, string GroupId)
{
    public string Scope => $"{Platform}:group:{GroupId}";

    public static PlayerWatchGroup? Create(string? platform, string? groupId)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(groupId)) return null;
        platform = platform.Trim().ToLowerInvariant();
        groupId = groupId.Trim();
        if (platform.Length > 32 || groupId.Length > 80
            || platform.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            || groupId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return null;
        return new(platform, groupId);
    }

    public static PlayerWatchGroup? Parse(string scope)
    {
        var index = scope.IndexOf(":group:", StringComparison.Ordinal);
        return index > 0 ? Create(scope[..index], scope[(index + 7)..]) : null;
    }
}

public sealed record PlayerSubscriptionInfo(string Id, int UserId, string? Username, int GameMode,
    string Scope, string? Platform, string? GroupId, long StartCursor, DateTimeOffset ExpiresAt,
    bool IsExpired, DateTimeOffset? CheckedAt);
public sealed record PlayerSubscriptionPage(int Page, int PageSize, int Total, List<PlayerSubscriptionInfo> Items);
public sealed record PlayerGroupInfo(string Platform, string GroupId, int SubscriptionCount, int UserCount);
public sealed record PlayerGroupPage(int Page, int PageSize, int Total, List<PlayerGroupInfo> Items);
public sealed record PlayerSubscriptionsRemoved(int Removed);
