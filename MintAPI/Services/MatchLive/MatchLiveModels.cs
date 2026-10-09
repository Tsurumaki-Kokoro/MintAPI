using MintOsuApi.Models;

namespace MintAPI.Services.MatchLive;

public sealed class MatchLiveOptions
{
    public int PollSeconds { get; set; } = 10;
    public int PollConcurrency { get; set; } = 2;
    public int StateTtlHours { get; set; } = 48;
    public int RequestBudgetPerMinute { get; set; } = 24;
    public int MaxMatches { get; set; } = 4;
    public int MaxSubscriptionsPerScope { get; set; } = 3;
    public int LeaseMinutes { get; set; } = 30;
    public int RetentionMinutes { get; set; } = 60;
    public int MaxUpdates { get; set; } = 1000;
    public int MockAutoAdvanceSeconds { get; set; } = 30;
    public bool MockEnabled { get; set; }
    public bool IsValid() => PollSeconds >= 10 && PollConcurrency is >= 1 and <= 64 && StateTtlHours is >= 1 and <= 87600 && RequestBudgetPerMinute is >= 1 and <= 1000 && MaxMatches is >= 1 and <= 50 &&
        MaxSubscriptionsPerScope is >= 1 and <= 10 && LeaseMinutes >= 1 && RetentionMinutes >= 1 && MaxUpdates >= 100 && (MockAutoAdvanceSeconds == 0 || MockAutoAdvanceSeconds >= 10);
}

public sealed record LiveSubscription(string Id, string Scope, DateTimeOffset ExpiresAt);
public sealed record LiveUpdate(long Revision, string Type, long? EventId, int? GameId,
    string Text, DateTimeOffset Timestamp, MatchEvent? Event);
public sealed record LiveGameState(int GameId, bool Ready);
public sealed record LiveSnapshot(int MatchId, string Name, string Status, bool IsMock, long Revision,
    DateTimeOffset? LastSuccess, string? Error, MatchGame? CurrentGame, int CompletedGames, IReadOnlyList<UserCompact> Users, IReadOnlyList<LiveGameState>? Games = null);
public sealed record LiveSubscriptionResult(string SubscriptionId, DateTimeOffset ExpiresAt, LiveSnapshot Snapshot, long Cursor);
public sealed record LiveUpdatesResult(LiveSnapshot Snapshot, long Cursor, IReadOnlyList<LiveUpdate> Updates);

// Redis payload includes upstream state separately from the client-facing revision log.
public sealed class LiveRoom
{
    public int MatchId { get; set; }
    public bool IsMock { get; set; }
    public MatchResponse Match { get; set; } = new();
    public long Revision { get; set; }
    public List<LiveUpdate> Updates { get; set; } = [];
    public Dictionary<string, LiveSubscription> Subscriptions { get; set; } = [];
    public DateTimeOffset? LastSuccess { get; set; }
    public DateTimeOffset NextPoll { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? Error { get; set; }
    public int Failures { get; set; }
    public DateTimeOffset MockNextAdvance { get; set; }
    public int MockStage { get; set; }
    public bool Initialized { get; set; }
}

public sealed class LiveConflictException(string message) : Exception(message);
public sealed class LiveGoneException(string message) : Exception(message);
