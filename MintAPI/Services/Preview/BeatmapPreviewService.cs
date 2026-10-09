using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

using MintAPI.Configuration;

namespace MintAPI.Services.Preview;

public sealed class BeatmapPreviewService : IBeatmapPreviewService
{
    private readonly BeatmapPreviewOptions _options;
    private readonly IOsuApiService _osuApi;
    private readonly IBeatmapFileService _beatmapFile;
    private readonly IPreviewCliRunner _runner;
    private readonly ILogger<BeatmapPreviewService> _logger;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim[] _requestLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly string _cacheRoot;
    private readonly string _executable;
    private readonly string _engineHash;
    private readonly CachePolicyOptions _cache;

    public BeatmapPreviewService(IOptions<BeatmapPreviewOptions> options, IConfiguration config,
        IOsuApiService osuApi, IBeatmapFileService beatmapFile, IPreviewCliRunner runner,
        ILogger<BeatmapPreviewService> logger, StoragePaths? paths = null)
    {
        _options = options.Value;
        _osuApi = osuApi;
        _beatmapFile = beatmapFile;
        _runner = runner;
        _logger = logger;
        _slots = new SemaphoreSlim(_options.MaxConcurrency);
        var executable = _options.ExecutablePath;
        if (OperatingSystem.IsWindows() && !Path.HasExtension(executable)) executable += ".exe";
        _executable = Path.GetFullPath(executable, AppContext.BaseDirectory);
        _cacheRoot = Path.Combine((paths ?? new StoragePaths(config)).CacheDirectory, "beatmap", "preview");
        _cache = config.GetSection("CachePolicy").Get<CachePolicyOptions>() ?? new();
        _engineHash = File.Exists(_executable) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_executable))) : "missing";
    }

    public async Task<BeatmapPreviewResult> GenerateAsync(BeatmapPreviewRequest request, CancellationToken cancellationToken)
    {
        request = PreviewRequestValidator.Normalize(request, _options);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.Format == "mp4" ? _options.VideoTimeoutSeconds : _options.ImageTimeoutSeconds));
        try { return await GenerateCoreAsync(request, timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RenderTimeoutException();
        }
    }

    private async Task<BeatmapPreviewResult> GenerateCoreAsync(BeatmapPreviewRequest request, CancellationToken token)
    {
        if (!File.Exists(_executable)) throw new PreviewFailedException("Preview CLI is not installed.");
        // Keep deployment configuration deterministic and represented in the cache key.
        if (File.Exists(Path.Combine(Path.GetDirectoryName(_executable)!, "config.yml")))
            throw new PreviewFailedException("Remove the unmanaged config.yml beside the preview CLI.");
        var cliConfig = JsonSerializer.Serialize(new { paths = new {
            CONFIG_DIR = Path.Combine(_cacheRoot, "config"), CACHE_DIR = Path.Combine(_cacheRoot, "downloads")
        }});
        var key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request, _engineHash, cliConfig, selector = BeatmapHardestSegmentSelector.AlgorithmVersion })));
        var directory = Path.Combine(_cacheRoot, "artifacts", key);
        var outputPath = Path.Combine(directory, $"preview.{request.Format}");
        var contentType = request.Format switch { "gif" => "image/gif", "png" => "image/png", _ => "video/mp4" };
        var result = new BeatmapPreviewResult(outputPath, contentType);
        var requestLock = _requestLocks[Convert.ToInt32(key[..2], 16) % _requestLocks.Length];
        await requestLock.WaitAsync(token);
        try
        {
            if (IsCached(outputPath)) return result;
            if (!await _slots.WaitAsync(0, token)) throw new RenderBusyException();
            try
            {
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(Path.Combine(_cacheRoot, "config"));
                var beatmap = await _osuApi.GetBeatmapAsync(request.BeatmapId).WaitAsync(token);
                PreviewRequestValidator.ValidateMode(request, (int)beatmap.Mode);
                var arguments = new List<string> {
                    $"--bid={request.BeatmapId}", $"--fmt={request.Format}", "--no-log",
                    $"--output-dir={directory}", $"--config={cliConfig}"
                };
                if (request.Format != "mp4")
                {
                    var input = await _beatmapFile.GetOsuFilePathAsync(beatmap.BeatmapsetId, request.BeatmapId).WaitAsync(token);
                    arguments.Add($"--input-file={Path.GetFullPath(input)}");
                    var includePreview = request.Selection == "auto" && request.TimePoints.Length == 0 &&
                        request.Format == "gif" && (request.Convert is null || request.Convert == ((int)beatmap.Mode switch { 0 => "standard", 1 => "taiko", 2 => "ctb", _ => "mania" }));
                    if (request.Selection == "hardest" || includePreview)
                    {
                        var selection = BeatmapHardestSegmentSelector.Select(input, request.Mods, request.DurationSeconds!.Value, includePreview);
                        token.ThrowIfCancellationRequested();
                        if (selection.Segments.Length == 0)
                            throw new PreviewValidationException("No playable strain windows were found.");
                        request = request with { TimePoints = selection.Segments.Select(segment => segment.StartSeconds.ToString("R", CultureInfo.InvariantCulture)).ToArray() };
                        // Exact capacity prevents the upstream selector from adding unrelated auto segments.
                        var rows = selection.Segments.Length == 4 ? 2 : 1;
                        cliConfig = JsonSerializer.Serialize(new {
                            paths = new { CONFIG_DIR = Path.Combine(_cacheRoot, "config"), CACHE_DIR = Path.Combine(_cacheRoot, "downloads") },
                            render = new Dictionary<string, object> {
                                [(int)beatmap.Mode switch { 0 => "standard", 1 => "taiko", 2 => "catch", _ => "mania" }] = new { gif = new { structure = (int)beatmap.Mode switch {
                                    1 => (object)new { ROW_COUNT = selection.Segments.Length },
                                    3 => new { IMAGES_PER_ROW = selection.Segments.Length },
                                    _ => new { ROW_COUNT = rows, IMAGES_PER_ROW = selection.Segments.Length / rows }
                                } } }
                            }
                        });
                        arguments.RemoveAll(argument => argument.StartsWith("--config="));
                        arguments.Add($"--config={cliConfig}");
                        await File.WriteAllTextAsync(Path.Combine(directory, "selection.json"), JsonSerializer.Serialize(selection,
                            new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }), token);
                        _logger.LogInformation("Native mode selected segments for {BeatmapId}: {Times}", request.BeatmapId, string.Join(", ", request.TimePoints));
                    }
                }
                if (request.Convert != null) arguments.Add($"--convert={request.Convert}");
                arguments.AddRange(request.Mods.Select(mod => $"--mod={mod}"));
                arguments.AddRange(request.TimePoints.Select(point => $"--time-points={point}"));
                if (request.DurationSeconds.HasValue)
                    arguments.Add($"--duration-time={request.DurationSeconds.Value.ToString("R", CultureInfo.InvariantCulture)}");
                if (request.Format != "png") arguments.Add("--fps=30");
                var output = await _runner.RunAsync(_executable, arguments, token);
                if (output.ExitCode != 0)
                {
                    _logger.LogWarning("Preview CLI failed for {BeatmapId}: {Error}", request.BeatmapId, output.StandardError);
                    throw new PreviewFailedException("Preview generation failed.");
                }
                string artifact;
                try
                {
                    using var json = JsonDocument.Parse(output.StandardOutput);
                    if (json.RootElement.GetProperty("status").GetString() != "success") throw new JsonException("CLI status is not success.");
                    artifact = Path.GetFullPath(json.RootElement.GetProperty("preview-img").GetString()!);
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException)
                {
                    throw new PreviewFailedException("Preview CLI returned an invalid result.");
                }
                var relative = Path.GetRelativePath(directory, artifact);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) ||
                    Path.GetExtension(artifact) != "." + request.Format || !IsCached(artifact))
                    throw new PreviewFailedException("Preview CLI returned an invalid artifact.");
                File.Move(artifact, outputPath, overwrite: true);
                return result;
            }
            finally { _slots.Release(); }
        }
        finally { requestLock.Release(); }
    }

    public async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        var acquired = 0;
        try
        {
            foreach (var requestLock in _requestLocks)
            {
                await requestLock.WaitAsync(cancellationToken);
                acquired++;
            }
            foreach (var name in new[] { "artifacts", "downloads" })
            {
                var directory = Path.Combine(_cacheRoot, name);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
        finally
        {
            for (var i = 0; i < acquired; i++) _requestLocks[i].Release();
        }
    }

    private bool IsCached(string path) => CachePolicyOptions.IsFresh(path, _cache.PreviewArtifactHours) && new FileInfo(path).Length > 0;
}
