using System.Text.RegularExpressions;
using MintAPI.Configuration;

namespace MintAPI.Services;

public partial class BeatmapFileService : IBeatmapFileService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BeatmapFileService> _logger;
    private readonly string _cacheDir;
    private readonly DownloadOptions _downloads;
    private readonly CachePolicyOptions _cache;

    public BeatmapFileService(IHttpClientFactory httpClientFactory, ILogger<BeatmapFileService> logger, IConfiguration config, StoragePaths? paths = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDir = (paths ?? new StoragePaths(config)).CacheDirectory;
        _downloads = DownloadOptions.Read(config);
        _cache = config.GetSection("CachePolicy").Get<CachePolicyOptions>() ?? new();
    }

    private string OsuFileDir(int setId) =>
        Path.Combine(_cacheDir, "beatmap", "osu_file", setId.ToString());

    public async Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId)
    {
        var dir = OsuFileDir(beatmapSetId);
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, $"{beatmapId}.osu");

        if (CachePolicyOptions.IsFresh(filePath, _cache.BeatmapFileHours))
            return filePath;

        _logger.LogInformation("Downloading osu file for beatmap {BeatmapId}", beatmapId);
        var urls = _downloads.BeatmapSources.Select(source => DownloadOptions.Expand(source, beatmapId, beatmapSetId)).ToArray();

        var content = await DownloadFirstSuccessfulAsync(urls);
        await AtomicCacheFile.WriteAsync(filePath, content);
        return filePath;
    }

    public async Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null)
    {
        var dir = OsuFileDir(setId);
        Directory.CreateDirectory(dir);
        var fileName = bgName ?? "set.jpg";
        var filePath = Path.Combine(dir, fileName);

        if (CachePolicyOptions.IsFresh(filePath, _cache.BackgroundHours))
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
            await AtomicCacheFile.WriteAsync(filePath, content);

        return content ?? [];
    }

    public async Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default)
    {
        if (setId <= 0) return null;
        var filePath = Path.Combine(OsuFileDir(setId), "list-cover.jpg");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_downloads.ListCoverTimeoutSeconds));
        try
        {
            if (CachePolicyOptions.IsFresh(filePath, _cache.ListCoverHours)) return await File.ReadAllBytesAsync(filePath, cancellationToken);
            using var response = await _httpClientFactory.CreateClient("Downloads").GetAsync(
                DownloadOptions.Expand(_downloads.CoverUrlTemplate, setId: setId),
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
        var urls = _downloads.BackgroundSources.Select(source => DownloadOptions.Expand(source, mapId, setId, bgName)).ToArray();
        try { return await DownloadFirstSuccessfulAsync(urls, imageOnly: true); }
        catch { return null; }
    }

    private async Task<byte[]?> TryDownloadSetBgAsync(int setId)
    {
        var urls = new[] { DownloadOptions.Expand(_downloads.CoverUrlTemplate, setId: setId) };
        try { return await DownloadFirstSuccessfulAsync(urls, imageOnly: true); }
        catch { return null; }
    }

    private async Task<byte[]?> TryDownloadSeasonalBgAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Downloads");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_downloads.TimeoutSeconds));
            using var resp = await client.GetAsync(_downloads.SeasonalBackgroundsUrl, timeout.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(timeout.Token);
            var match = Regex.Match(json, "\"url\"\\s*:\\s*\"([^\"]+)\"");
            if (!match.Success) return null;
            return await client.GetByteArrayAsync(match.Groups[1].Value, timeout.Token);
        }
        catch { return null; }
    }

    private async Task<byte[]> DownloadFirstSuccessfulAsync(string[] urls, bool imageOnly = false)
    {
        var client = _httpClientFactory.CreateClient("Downloads");
        foreach (var url in urls)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_downloads.TimeoutSeconds));
                using var resp = await client.GetAsync(url, timeout.Token);
                if (!resp.IsSuccessStatusCode) continue;
                if (imageOnly)
                {
                    var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (!ct.StartsWith("image/")) continue;
                }
                return await resp.Content.ReadAsByteArrayAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to download from source {Host}", new Uri(url).Host);
            }
        }
        throw new InvalidOperationException($"All download URLs failed: {string.Join(", ", urls)}");
    }

    [GeneratedRegex(@"\d,\d,""(.+)""")]
    private static partial Regex BgRegex();
}
