using System.Text.RegularExpressions;

namespace HitCircleAPI.Services;

public partial class BeatmapFileService : IBeatmapFileService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BeatmapFileService> _logger;
    private readonly string _cacheDir;

    public BeatmapFileService(IHttpClientFactory httpClientFactory, ILogger<BeatmapFileService> logger, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDir = config["CacheDir"] ?? "cache";
    }

    private string OsuFileDir(int setId) =>
        Path.Combine(_cacheDir, "beatmap", "osu_file", setId.ToString());

    public async Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId)
    {
        var dir = OsuFileDir(beatmapSetId);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{beatmapId}.osu");

        if (File.Exists(filePath))
            return filePath;

        _logger.LogInformation("Downloading osu file for beatmap {BeatmapId}", beatmapId);
        var urls = new[]
        {
            $"https://osu.ppy.sh/osu/{beatmapId}",
            $"https://api.osu.direct/osu/{beatmapId}"
        };

        var content = await DownloadFirstSuccessfulAsync(urls);
        await File.WriteAllBytesAsync(filePath, content);
        return filePath;
    }

    public async Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null)
    {
        var dir = OsuFileDir(setId);
        Directory.CreateDirectory(dir);
        var fileName = bgName ?? "set.jpg";
        var filePath = Path.Combine(dir, fileName);

        if (File.Exists(filePath))
            return await File.ReadAllBytesAsync(filePath);

        byte[]? content = null;
        if (bgName != null)
        {
            content = await TryDownloadBgAsync(mapId, setId, bgName);
        }
        else
        {
            content = await TryDownloadSetBgAsync(setId);
        }

        if (content == null || content.Length == 0)
        {
            _logger.LogWarning("Failed to get beatmap background for set {SetId}, falling back to seasonal", setId);
            content = await TryDownloadSeasonalBgAsync();
        }

        if (content != null && content.Length > 0)
            await File.WriteAllBytesAsync(filePath, content);

        return content ?? [];
    }

    public async Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default)
    {
        if (setId <= 0) return null;
        var filePath = Path.Combine(OsuFileDir(setId), "list-cover.jpg");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (File.Exists(filePath)) return await File.ReadAllBytesAsync(filePath, cancellationToken);
            using var response = await _httpClientFactory.CreateClient().GetAsync(
                $"https://assets.ppy.sh/beatmaps/{setId}/covers/cover@2x.jpg",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true)
                return null;
            var data = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (data.Length == 0) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            // Publish a complete file so another request never reads a partially written image.
            var tempFile = filePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempFile, data, timeout.Token);
                File.Move(tempFile, filePath, overwrite: true);
            }
            finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
            return data;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to get list cover for set {SetId}", setId);
            return null;
        }
    }

    public string GetBgFilename(string osuFilePath)
    {
        var text = File.ReadAllText(osuFilePath, System.Text.Encoding.UTF8);
        foreach (Match m in BgRegex().Matches(text))
        {
            var candidate = m.Groups[1].Value.Trim();
            if (candidate.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                candidate.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        return "mapbg.png";
    }

    private async Task<byte[]?> TryDownloadBgAsync(int mapId, int setId, string bgName)
    {
        var urls = new[]
        {
            $"https://api.osu.direct/media/background/{mapId}",
            $"https://subapi.nerinyan.moe/bg/{mapId}",
            $"https://dl.sayobot.cn/beatmaps/files/{setId}/{Uri.EscapeDataString(bgName)}"
        };
        try { return await DownloadFirstSuccessfulAsync(urls, imageOnly: true); }
        catch { return null; }
    }

    private async Task<byte[]?> TryDownloadSetBgAsync(int setId)
    {
        var urls = new[] { $"https://assets.ppy.sh/beatmaps/{setId}/covers/cover@2x.jpg" };
        try { return await DownloadFirstSuccessfulAsync(urls, imageOnly: true); }
        catch { return null; }
    }

    private async Task<byte[]?> TryDownloadSeasonalBgAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var resp = await client.GetAsync("https://osu.ppy.sh/api/v2/seasonal-backgrounds");
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var match = Regex.Match(json, "\"url\"\\s*:\\s*\"([^\"]+)\"");
            if (!match.Success) return null;
            return await client.GetByteArrayAsync(match.Groups[1].Value);
        }
        catch { return null; }
    }

    private async Task<byte[]> DownloadFirstSuccessfulAsync(string[] urls, bool imageOnly = false)
    {
        var client = _httpClientFactory.CreateClient();
        foreach (var url in urls)
        {
            try
            {
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) continue;
                if (imageOnly)
                {
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (!ct.StartsWith("image/")) continue;
                }
                return await resp.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to download from {Url}: {Message}", url, ex.Message);
            }
        }
        throw new InvalidOperationException($"All download URLs failed: {string.Join(", ", urls)}");
    }

    [GeneratedRegex(@"\d,\d,""(.+)""")]
    private static partial Regex BgRegex();
}
