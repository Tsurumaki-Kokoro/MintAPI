namespace MintAPI.Services.Preview;

public sealed class BeatmapPreviewOptions
{
    public string ExecutablePath { get; set; } = "tools/osu-preview/osu-beatmap-preview-cli";
    public int MaxConcurrency { get; set; } = 1;
    public int ImageTimeoutSeconds { get; set; } = 120;
    public int VideoTimeoutSeconds { get; set; } = 300;
    public int MaxVideoDurationSeconds { get; set; } = 60;
    public int MaxGifDurationSeconds { get; set; } = 12;
    public bool IsValid() => !string.IsNullOrWhiteSpace(ExecutablePath) &&
        MaxConcurrency > 0 && ImageTimeoutSeconds > 0 && VideoTimeoutSeconds > 0 &&
        MaxVideoDurationSeconds is >= 30 and <= 60 && MaxGifDurationSeconds is >= 6 and <= 12;
}
