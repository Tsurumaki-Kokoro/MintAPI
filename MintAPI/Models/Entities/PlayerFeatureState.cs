namespace MintAPI.Models.Entities;

public sealed class BestPlaySnapshot
{
    public int UserId { get; set; }
    public int GameMode { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string Payload { get; set; } = "[]";
}

public sealed class PlayerWatch
{
    public int UserId { get; set; }
    public int GameMode { get; set; }
    public long Revision { get; set; }
    public long FloorCursor { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class PlayerSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    public int GameMode { get; set; }
    public string Scope { get; set; } = "";
    public string? Platform { get; set; }
    public string? GroupId { get; set; }
    public long StartCursor { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PlayerWatchEvent
{
    public int UserId { get; set; }
    public int GameMode { get; set; }
    public long Cursor { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public string Text { get; set; } = "";
}
