using System.Net;
using System.Net.Http.Headers;
using HitCircleAPI.Controllers;
using HitCircleAPI.Middleware;
using HitCircleAPI.Services;
using HitCircleAPI.Services.Preview;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HitCircleAPI.Tests.Integration;

public sealed class BeatmapPreviewControllerTests
{
    [Fact]
    public async Task Endpoints_return_media_range_and_validation_errors()
    {
        var path = Path.Combine(Path.GetTempPath(), "preview-http-" + Guid.NewGuid() + ".mp4");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        var preview = new FakePreview(path);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IBeatmapPreviewService>(preview);
        builder.Services.AddControllers().AddApplicationPart(typeof(BeatmapPreviewController).Assembly);
        builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var image = await http.GetAsync("/beatmap/preview/image?beatmap_id=1&format=png&mods=hr&mods=hd");
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
            Assert.Equal(["hr", "hd"], preview.LastRequest!.Mods);
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/beatmap/preview/video?beatmap_id=1");
            rangeRequest.Headers.Range = new RangeHeaderValue(0, 9);
            using var video = await http.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, video.StatusCode);
            Assert.Equal("video/mp4", video.Content.Headers.ContentType!.MediaType);
            Assert.Equal(10, (await video.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal(30, preview.LastRequest!.DurationSeconds);
            Assert.Equal(["preview"], preview.LastRequest.TimePoints);
            using var invalid = await http.GetAsync("/beatmap/preview/image?beatmap_id=1&format=mp4");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            preview.Error = new PreviewValidationException("invalid");
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/beatmap/preview/video?beatmap_id=1")).StatusCode);
            preview.Error = new RenderBusyException();
            using var busy = await http.GetAsync("/beatmap/preview/video?beatmap_id=1");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.NotNull(busy.Headers.RetryAfter);
        }
        finally { await app.StopAsync(); File.Delete(path); }
    }

    private sealed class FakePreview(string path) : IBeatmapPreviewService
    {
        public BeatmapPreviewRequest? LastRequest;
        public Exception? Error;
        public Task ClearCacheAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BeatmapPreviewResult> GenerateAsync(BeatmapPreviewRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (Error != null) throw Error;
            return Task.FromResult(new BeatmapPreviewResult(path, request.Format == "mp4" ? "video/mp4" : "image/" + request.Format));
        }
    }
}
