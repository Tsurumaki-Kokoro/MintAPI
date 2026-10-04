using System.Text.Json;
using MintAPI.Services;
using MintAPI.Services.Preview;
using MintAPI.Tests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public sealed class BeatmapPreviewTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "preview-tests-" + Guid.NewGuid());
    private readonly RecordingOsuApiService _api = new() { BeatmapHandler = id => new Beatmap { Id = id, BeatmapsetId = 7, Mode = GameMode.Osu } };

    [Theory]
    [InlineData("mp4", 61)]
    [InlineData("gif", 12.001)]
    [InlineData("gif", 0)]
    [InlineData("gif", double.NaN)]
    public void Invalid_duration_is_rejected(string format, double duration) =>
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.Normalize(new(1, format, null, [], [], duration), new()));

    [Theory]
    [InlineData(null, 6)]
    [InlineData(7.0, 7.0)]
    [InlineData(12.0, 12.0)]
    public void Gif_duration_defaults_to_six_and_accepts_up_to_twelve(double? requested, double expected)
    {
        var normalized = PreviewRequestValidator.Normalize(new(1, "gif", null, [], [], requested), new());
        Assert.Equal(expected, normalized.DurationSeconds);
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(12, true)]
    [InlineData(5, false)]
    [InlineData(13, false)]
    public void Gif_limit_configuration_stays_within_supported_range(int limit, bool valid) =>
        Assert.Equal(valid, new BeatmapPreviewOptions { MaxGifDurationSeconds = limit }.IsValid());

    [Fact]
    public void Mode_specific_invalid_requests_are_rejected()
    {
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.ValidateMode(new(1, "png", null, [], [], 6), 0));
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.ValidateMode(new(1, "gif", "mania", [], [], 6), 1));
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.ValidateMode(new(1, "png", null, ["dt"], [], null), 3));
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.Normalize(new(1, "gif", null, ["dt", "ht"], [], 6), new()));
        Assert.Throws<PreviewValidationException>(() => PreviewRequestValidator.Normalize(new(1, "gif", null, ["dacs99"], [], 6), new()));
    }

    [Fact]
    public async Task Identical_requests_share_one_render_and_cached_result()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner(async (args, token) => { started.SetResult(); await release.Task.WaitAsync(token); return Artifact(args); });
        var service = Create(runner);
        var first = service.GenerateAsync(Request(), default);
        await started.Task;
        var second = service.GenerateAsync(Request(), default);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, runner.Calls);
        Assert.Equal(1, _api.CallCount);
    }

    [Fact]
    public async Task Artifact_outside_request_directory_is_rejected()
    {
        var runner = new FakeRunner((_, _) => Task.FromResult(new PreviewCliOutput(0,
            JsonSerializer.Serialize(new Dictionary<string, object> { ["status"] = "success", ["preview-img"] = Path.Combine(_directory, "engine") }), "")));
        await Assert.ThrowsAsync<PreviewFailedException>(() => Create(runner).GenerateAsync(Request(), default));
    }

    [Fact]
    public async Task Timeout_cancels_runner_and_releases_slot_for_next_request()
    {
        var runner = new FakeRunner(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return null!; });
        var service = Create(runner, timeout: 1);
        await Assert.ThrowsAsync<RenderTimeoutException>(() => service.GenerateAsync(Request(), default));
        runner.Handler = (args, _) => Task.FromResult(Artifact(args));
        Assert.True(File.Exists((await service.GenerateAsync(Request(), default)).Path));
    }

    [Fact]
    public async Task Video_uses_online_source_and_bounded_duration()
    {
        var runner = new FakeRunner((args, _) => Task.FromResult(Artifact(args)));
        var result = await Create(runner).GenerateAsync(new(1, "mp4", null, [], ["preview"], null), default);
        Assert.Equal("video/mp4", result.ContentType);
        Assert.Contains("--duration-time=30", runner.Arguments);
        Assert.DoesNotContain(runner.Arguments, arg => arg.StartsWith("--input-file="));
    }

    [Fact]
    public async Task Hardest_uses_native_strains_and_passes_exact_four_panels_to_cli()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"), Path.Combine(_directory, "map with spaces.osu"));
        var runner = new FakeRunner((args, _) => Task.FromResult(Artifact(args)));
        var result = await Create(runner).GenerateAsync(new(1, "gif", null, ["dt1.25"], [], 6, "hardest"), default);
        var timePoints = runner.Arguments.Where(argument => argument.StartsWith("--time-points=")).ToArray();
        Assert.Equal(4, timePoints.Length);
        Assert.DoesNotContain("--time-points=preview", timePoints);
        using var config = JsonDocument.Parse(runner.Arguments.Single(argument => argument.StartsWith("--config="))[9..]);
        var structure = config.RootElement.GetProperty("render").GetProperty("standard").GetProperty("gif").GetProperty("structure");
        Assert.Equal(4, structure.GetProperty("ROW_COUNT").GetInt32() * structure.GetProperty("IMAGES_PER_ROW").GetInt32());
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(result.Path)!, "selection.json")));
        var segments = report.RootElement.GetProperty("Segments").EnumerateArray().ToArray();
        Assert.Equal(4, segments.Length);
        foreach (var segment in segments)
            Assert.Equal(7.5, segment.GetProperty("EndSeconds").GetDouble() - segment.GetProperty("StartSeconds").GetDouble(), 6);
    }

    [Fact]
    public async Task Default_standard_gif_contains_preview_time_and_three_hard_windows()
    {
        var runner = new FakeRunner((args, _) => Task.FromResult(Artifact(args)));
        var result = await Create(runner).GenerateAsync(Request(), default);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(result.Path)!, "selection.json")));
        var segments = report.RootElement.GetProperty("Segments").EnumerateArray().ToArray();
        Assert.Equal(4, segments.Length);
        var preview = Assert.Single(segments, segment => segment.GetProperty("IsPreview").GetBoolean());
        Assert.Equal((100652 - 611) / 1000.0, preview.GetProperty("StartSeconds").GetDouble(), 6);
        Assert.Equal(3, segments.Count(segment => !segment.GetProperty("IsPreview").GetBoolean()));
    }


    [Theory]
    [InlineData(1, "1028484", "taiko", "auto")]
    [InlineData(2, "2118524", "catch", "auto")]
    [InlineData(3, "1638954", "mania", "auto")]
    [InlineData(1, "1028484", "taiko", "hardest")]
    [InlineData(2, "2118524", "catch", "hardest")]
    [InlineData(3, "1638954", "mania", "hardest")]
    public async Task Native_modes_send_exact_selected_capacity(int mode, string id, string configMode, string strategy)
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", id + ".osu"), Path.Combine(_directory, "map with spaces.osu"));
        _api.BeatmapHandler = bid => new Beatmap { Id = bid, BeatmapsetId = 7, Mode = (GameMode)mode };
        var runner = new FakeRunner((args, _) => Task.FromResult(Artifact(args)));
        var result = await Create(runner).GenerateAsync(new(1, "gif", null, [], [], 6, strategy), default);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(result.Path)!, "selection.json")));
        var segments = report.RootElement.GetProperty("Segments").EnumerateArray().ToArray();
        Assert.Equal(4, segments.Length);
        Assert.Equal(strategy == "auto" ? 1 : 0, segments.Count(s => s.GetProperty("IsPreview").GetBoolean()));
        Assert.Equal(4, runner.Arguments.Count(a => a.StartsWith("--time-points=")));
        using var config = JsonDocument.Parse(runner.Arguments.Single(a => a.StartsWith("--config="))[9..]);
        var structure = config.RootElement.GetProperty("render").GetProperty(configMode).GetProperty("gif").GetProperty("structure");
        var capacity = mode switch { 1 => structure.GetProperty("ROW_COUNT").GetInt32(), 3 => structure.GetProperty("IMAGES_PER_ROW").GetInt32(), _ => structure.GetProperty("ROW_COUNT").GetInt32() * structure.GetProperty("IMAGES_PER_ROW").GetInt32() };
        Assert.Equal(segments.Length, capacity);
    }

    private BeatmapPreviewService Create(FakeRunner runner, int timeout = 10)
    {
        Directory.CreateDirectory(_directory);
        var input = Path.Combine(_directory, "map with spaces.osu");
        if (!File.Exists(input)) File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"), input);
        var executable = Path.Combine(_directory, "engine");
        File.WriteAllText(executable, "test engine");
        return new(Options.Create(new BeatmapPreviewOptions { ExecutablePath = executable, ImageTimeoutSeconds = timeout }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CacheDir"] = _directory }).Build(),
            _api, new FakeFiles(_directory), runner, NullLogger<BeatmapPreviewService>.Instance);
    }

    private static BeatmapPreviewRequest Request() => new(1, "gif", null, [], [], null);
    private static PreviewCliOutput Artifact(IReadOnlyList<string> args)
    {
        var directory = args.Single(arg => arg.StartsWith("--output-dir="))[13..];
        var format = args.Single(arg => arg.StartsWith("--fmt="))[6..];
        var path = Path.Combine(directory, "render." + format);
        File.WriteAllBytes(path, [1, 2, 3]);
        return new(0, JsonSerializer.Serialize(new Dictionary<string, object> { ["status"] = "success", ["preview-img"] = path }), "");
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeFiles(string directory) : IBeatmapFileService
    {
        public Task<string> GetOsuFilePathAsync(int beatmapSetId, int beatmapId) => Task.FromResult(Path.Combine(directory, "map with spaces.osu"));
        public Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null) => throw new NotImplementedException();
        public Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public string GetBgFilename(string osuFilePath) => throw new NotImplementedException();
    }
    private sealed class FakeRunner(Func<IReadOnlyList<string>, CancellationToken, Task<PreviewCliOutput>> handler) : IPreviewCliRunner
    {
        public Func<IReadOnlyList<string>, CancellationToken, Task<PreviewCliOutput>> Handler = handler;
        public int Calls;
        public IReadOnlyList<string> Arguments = [];
        public Task<PreviewCliOutput> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Arguments = arguments;
            return Handler(arguments, cancellationToken);
        }
    }
}
