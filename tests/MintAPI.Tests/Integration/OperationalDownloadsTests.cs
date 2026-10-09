using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Configuration;
using MintAPI.Services;
using MintAPI.Tests.Unit;

namespace MintAPI.Tests.Integration;

public sealed class OperationalDownloadsTests
{
    [Fact]
    public async Task Beatmap_sources_follow_configured_order_and_expired_files_refresh_atomically()
    {
        var root = TempRoot();
        var requests = new List<string>();
        var factory = new Clients(async (request, ct) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            await Task.Yield();
            return new HttpResponseMessage(request.RequestUri.Host == "first.example" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3]) };
        });
        var config = OperationalConfigurationTests.Config(new()
        {
            ["CacheDir"] = root,
            ["Downloads:BeatmapSources:0"] = "https://first.example/{beatmapId}",
            ["Downloads:BeatmapSources:1"] = "https://second.example/{beatmapId}",
            ["CachePolicy:BeatmapFileHours"] = "1"
        });
        try
        {
            var service = new BeatmapFileService(factory, NullLogger<BeatmapFileService>.Instance, config);
            var path = await service.GetOsuFilePathAsync(10, 20);
            Assert.Equal(["https://first.example/20", "https://second.example/20"], requests);
            await service.GetOsuFilePathAsync(10, 20);
            Assert.Equal(2, requests.Count);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            await service.GetOsuFilePathAsync(10, 20);
            Assert.Equal(4, requests.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task List_cover_deadline_cancels_download_and_caller_cancellation_propagates()
    {
        var root = TempRoot();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Clients(async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            throw new InvalidOperationException();
        });
        var config = OperationalConfigurationTests.Config(new()
        { ["CacheDir"] = root, ["Downloads:ListCoverTimeoutSeconds"] = "1" });
        try
        {
            var service = new BeatmapFileService(factory, NullLogger<BeatmapFileService>.Instance, config);
            Assert.Null(await service.GetListCoverAsync(10).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(cancelled.Task.IsCompletedSuccessfully);
            using var ct = new CancellationTokenSource();
            ct.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetListCoverAsync(20, ct.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Avatar_expiration_is_configurable_and_failed_refresh_keeps_old_content()
    {
        var root = TempRoot();
        var calls = 0;
        var factory = new Clients((_, _) =>
        {
            calls++;
            if (calls == 3) throw new HttpRequestException("unavailable");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([(byte)calls]) });
        });
        var config = OperationalConfigurationTests.Config(new() { ["CacheDir"] = root, ["CachePolicy:AvatarHours"] = "1" });
        try
        {
            var service = new ImageCacheService(factory, NullLogger<ImageCacheService>.Instance, config);
            Assert.Equal(new byte[] { 1 }, await service.GetAvatarAsync("https://a.ppy.sh/7.png", 7));
            var path = Path.Combine(root, "avatar", "7.png");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            Assert.Equal(new byte[] { 2 }, await service.GetAvatarAsync("https://a.ppy.sh/7.png", 7));
            Assert.Equal(new byte[] { 2 }, await service.GetAvatarAsync("https://a.ppy.sh/7.png", 7));
            Assert.Equal(2, calls);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            Assert.Equal(new byte[] { 2 }, await service.GetAvatarAsync("https://a.ppy.sh/7.png", 7));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mint-downloads-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        return root;
    }
    private sealed class Clients(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(send)) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
