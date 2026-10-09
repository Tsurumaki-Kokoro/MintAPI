using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintAPI.Configuration;
using MintAPI.Controllers;
using MintAPI.Errors;
using MintAPI.Services.Preview;
using MintAPI.Tests.TestDoubles;
using MintAPI.Tests.Unit;

namespace MintAPI.Tests.Integration;

public sealed class TaskStorageHttpTests
{
    [Fact]
    public async Task Tasks_use_custom_storage_preserve_uploaded_backgrounds_and_keep_error_contract()
    {
        var root = Path.Combine(Path.GetTempPath(), "mint-task-" + Guid.NewGuid());
        var config = OperationalConfigurationTests.Config(new()
        { ["CacheDir"] = Path.Combine(root, "cache"), ["Storage:LogDirectory"] = Path.Combine(root, "logs") });
        var paths = new StoragePaths(config);
        var preview = new Preview();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<IBeatmapPreviewService>(preview);
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>()).AddApplicationPart(typeof(TaskController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            foreach (var folder in new[] { "avatar", "user_banner", "badge", "beatmap/osu_file/1", "user_bg" })
            {
                var directory = Path.Combine(paths.CacheDirectory, folder);
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "1.png"), "image");
            }
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
            using var cleared = await http.PostAsync("/task/clear_cache", null);
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.True(preview.Cleared);
            Assert.False(Directory.Exists(Path.Combine(paths.CacheDirectory, "avatar")));
            Assert.False(Directory.Exists(Path.Combine(paths.CacheDirectory, "user_banner")));
            Assert.False(Directory.Exists(Path.Combine(paths.CacheDirectory, "badge")));
            Assert.Empty(Directory.GetDirectories(Path.Combine(paths.CacheDirectory, "beatmap", "osu_file")));
            Assert.True(File.Exists(Path.Combine(paths.CacheDirectory, "user_bg", "1.png")));
            using var missing = await http.PostAsync("/task/pack_logs", null);
            await ApiErrorAssertions.AssertAsync(missing, ErrorCatalog.RecordNotFound, paths.LogDirectory);
            Directory.CreateDirectory(paths.LogDirectory);
            await File.WriteAllTextAsync(Path.Combine(paths.LogDirectory, "service.log"), "custom log");
            http.DefaultRequestHeaders.Accept.Clear();
            using var packed = await http.PostAsync("/task/pack_logs", null);
            Assert.Equal(HttpStatusCode.OK, packed.StatusCode);
            using var archive = new ZipArchive(new MemoryStream(await packed.Content.ReadAsByteArrayAsync()));
            using var reader = new StreamReader(archive.GetEntry("service.log")!.Open());
            Assert.Equal("custom log", await reader.ReadToEndAsync());
            preview.Fail = true;
            http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
            using var failed = await http.PostAsync("/task/clear_cache", null);
            await ApiErrorAssertions.AssertAsync(failed, ErrorCatalog.InternalError, "private storage failure");
        }
        finally
        {
            await app.StopAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class Preview : IBeatmapPreviewService
    {
        public bool Cleared;
        public bool Fail;
        public Task ClearCacheAsync(CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("private storage failure");
            Cleared = true;
            return Task.CompletedTask;
        }
        public Task<BeatmapPreviewResult> GenerateAsync(BeatmapPreviewRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
