using System.Net;
using System.Net.Http.Headers;
using HitCircleAPI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HitCircleAPI.Tests.Unit;

public sealed class BeatmapListCoverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "list-cover-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Cover_is_cached_and_only_requests_the_official_set_image()
    {
        var requests = 0;
        var data = new byte[] { 0xff, 0xd8, 0xff, 0xdb, 1, 2, 3 };
        var service = Create(new Handler((request, _) =>
        {
            requests++;
            Assert.Equal("https://assets.ppy.sh/beatmaps/1885198/covers/cover@2x.jpg", request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        }));
        Assert.Equal(data, await service.GetListCoverAsync(1885198));
        Assert.Equal(data, await service.GetListCoverAsync(1885198));
        Assert.Equal(1, requests);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(_directory, "beatmap", "osu_file", "1885198", "list-cover.jpg")));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/jpeg")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task Missing_or_non_image_response_does_not_cache_an_unrelated_background(HttpStatusCode status, string type)
    {
        var requests = 0;
        var service = Create(new Handler((_, _) =>
        {
            requests++;
            var response = new HttpResponseMessage(status) { Content = new StringContent("Not an image") };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
            return Task.FromResult(response);
        }));
        Assert.Null(await service.GetListCoverAsync(1885198));
        Assert.Equal(1, requests);
        Assert.False(File.Exists(Path.Combine(_directory, "beatmap", "osu_file", "1885198", "list-cover.jpg")));
    }

    [Fact]
    public async Task Cancelled_request_propagates_cancellation()
    {
        var service = Create(new Handler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Should be cancelled");
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetListCoverAsync(1885198, cancellation.Token));
    }

    private BeatmapFileService Create(HttpMessageHandler handler) => new(new Factory(new HttpClient(handler)),
        NullLogger<BeatmapFileService>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CacheDir"] = _directory }).Build());

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
