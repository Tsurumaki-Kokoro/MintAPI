namespace HitCircleAPI.Services.Preview;

public record BeatmapPreviewRequest(int BeatmapId, string Format, string? Convert,
    string[] Mods, string[] TimePoints, double? DurationSeconds);
public record BeatmapPreviewResult(string Path, string ContentType);
public sealed class PreviewValidationException(string message) : Exception(message);
public sealed class PreviewFailedException(string message) : Exception(message);

public interface IBeatmapPreviewService
{
    Task ClearCacheAsync(CancellationToken cancellationToken);
    Task<BeatmapPreviewResult> GenerateAsync(BeatmapPreviewRequest request, CancellationToken cancellationToken);
}
