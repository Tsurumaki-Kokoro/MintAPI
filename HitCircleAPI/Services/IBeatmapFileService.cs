namespace HitCircleAPI.Services;

public interface IBeatmapFileService
{
    Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId);
    Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null);
    string GetBgFilename(string osuFilePath);
}
