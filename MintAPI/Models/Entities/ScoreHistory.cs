namespace MintAPI.Models.Entities;

public sealed class ScoreHistory
{
    public long ScoreId { get; set; }
    public int UserId { get; set; }
    public int BeatmapId { get; set; }
    public int GameMode { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public string Payload { get; set; } = "";
}

public sealed class HistoryTaskRun
{
    public string Job { get; set; } = "";
    public DateOnly Date { get; set; }
    public int FailedTargets { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}
