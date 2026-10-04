using System.Net;
using System.Net.Http.Headers;
using MintAPI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MintAPI.Tests.Unit;

public sealed class UserBannerCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "user-banner-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Changed_banner_url_refreshes_without_overwriting_uploaded_background()
    {
        var calls = 0;
        var service = Create(new Handler(request =>
        {
            calls++;
            return Image(request.RequestUri!.AbsolutePath.EndsWith("new.png") ? [2, 3] : [1, 2]);
        }));
        await service.SaveUserBackgroundAsync(42, [8, 9]);
        const string url = "https://assets.ppy.sh/user-profile-covers/42/old.png";
        Assert.Equal(new byte[] { 1, 2 }, await service.GetUserBannerAsync(url, 42));
        Assert.Equal(new byte[] { 1, 2 }, await service.GetUserBannerAsync(url, 42));
        Assert.Equal(new byte[] { 2, 3 }, await service.GetUserBannerAsync(url.Replace("old", "new"), 42));
        Assert.Equal(2, calls);
        Assert.Equal(new byte[] { 8, 9 }, await service.GetUserBackgroundAsync(42));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://assets.ppy.sh/banner.jpg")]
    [InlineData("https://other.example/banner.jpg")]
    public async Task Missing_or_non_official_url_does_not_make_a_request(string? url)
    {
        var service = Create(new Handler(_ => throw new InvalidOperationException("Must not download")));
        Assert.Null(await service.GetUserBannerAsync(url, 42));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/png")]
    [InlineData(HttpStatusCode.OK, "text/html")]
    public async Task Unavailable_or_non_image_banner_returns_null(HttpStatusCode status, string contentType)
    {
        var service = Create(new Handler(_ =>
        {
            var response = Image([1]);
            response.StatusCode = status;
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return response;
        }));
        Assert.Null(await service.GetUserBannerAsync("https://assets.ppy.sh/user-profile-covers/42/missing.png", 42));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.img", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Expired_banner_is_preserved_when_refresh_is_offline()
    {
        var offline = false;
        var service = Create(new Handler(_ => offline ? throw new HttpRequestException("Offline") : Image([1, 2])));
        const string url = "https://assets.ppy.sh/user-profile-covers/42/banner.png";
        await service.GetUserBannerAsync(url, 42);
        File.SetLastWriteTimeUtc(Directory.GetFiles(_directory, "*.img", SearchOption.AllDirectories).Single(), DateTime.UtcNow.AddDays(-2));
        offline = true;
        Assert.Equal(new byte[] { 1, 2 }, await service.GetUserBannerAsync(url, 42));
    }

    private static HttpResponseMessage Image(byte[] data)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }

    private ImageCacheService Create(HttpMessageHandler handler) => new(new Factory(new HttpClient(handler)),
        NullLogger<ImageCacheService>.Instance,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CacheDir"] = _directory }).Build());
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
