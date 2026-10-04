namespace MintAPI.Services;

public interface IBeatmapFileService
{
    Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId);
    Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null);
    Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default);
    string GetBgFilename(string osuFilePath);
}
