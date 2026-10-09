using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MintAPI.Configuration;

namespace MintAPI.Tests.Unit;

public sealed class OperationalConfigurationTests
{
    [Fact]
    public void Relative_paths_use_content_root_and_token_default_is_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "mint-paths-" + Guid.NewGuid());
        var config = Config(new() { ["CacheDir"] = "data/cache", ["Storage:LogDirectory"] = "data/logs" });
        var paths = new StoragePaths(config, new Environment(root));
        Assert.Equal(Path.Combine(root, "data", "cache"), paths.CacheDirectory);
        Assert.Equal(Path.Combine(root, "data", "logs"), paths.LogDirectory);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "token_cache"), paths.TokenCacheDirectory);
        config["Storage:TokenCacheDirectory"] = "data/tokens";
        Assert.Equal(Path.Combine(root, "data", "tokens"), new StoragePaths(config, new Environment(root)).TokenCacheDirectory);
    }

    [Theory]
    [InlineData("cache", "cache")]
    [InlineData("cache", "cache/logs")]
    [InlineData("data/cache", "data")]
    public void Overlapping_cache_and_log_paths_are_rejected(string cache, string logs)
    {
        Assert.Throws<ArgumentException>(() => new StoragePaths(Config(new() { ["CacheDir"] = cache, ["Storage:LogDirectory"] = logs })));
    }

    [Fact]
    public void Custom_download_sources_replace_defaults_in_direct_and_injected_options()
    {
        var config = Config(new() { ["Downloads:BeatmapSources:0"] = "https://mirror.example/osu/{beatmapId}" });
        Assert.Equal(["https://mirror.example/osu/{beatmapId}"], DownloadOptions.Read(config).BeatmapSources);
        var services = new ServiceCollection();
        services.AddOperationalConfiguration(config, new Environment(Path.GetTempPath()));
        using var provider = services.BuildServiceProvider();
        Assert.Equal(["https://mirror.example/osu/{beatmapId}"], provider.GetRequiredService<IOptions<DownloadOptions>>().Value.BeatmapSources);
    }

    [Theory]
    [InlineData("Downloads:TimeoutSeconds", "0")]
    [InlineData("Downloads:CoverUrlTemplate", "http://mirror.example/{setId}")]
    [InlineData("Downloads:BeatmapSources:0", "https://mirror.example/{unknown}")]
    [InlineData("CachePolicy:AvatarHours", "0")]
    [InlineData("CachePolicy:BadgeHours", "-1")]
    [InlineData("Concurrency:CoverDownload", "0")]
    [InlineData("LogFiles:FileName", "../secret.log")]
    [InlineData("LogFiles:RollingInterval", "99")]
    public async Task Invalid_operational_configuration_fails_host_start(string key, string value)
    {
        using var host = new HostBuilder().ConfigureServices((context, services) =>
            services.AddOperationalConfiguration(Config(new() { [key] = value }), context.HostingEnvironment)).Build();
        var error = await Record.ExceptionAsync(() => host.StartAsync());
        Assert.NotNull(error);
        Assert.True(error is OptionsValidationException or AggregateException, error.ToString());
    }

    [Fact]
    public void Log_files_honor_directory_level_and_template_and_can_be_disabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "mint-logs-" + Guid.NewGuid());
        var config = Config(new()
        {
            ["Storage:LogDirectory"] = root, ["LogFiles:ConsoleEnabled"] = "false",
            ["LogFiles:FileName"] = "service.log", ["LogFiles:RollingInterval"] = "Infinite",
            ["LogFiles:OutputTemplate"] = "{TraceId}|{Message:lj}{NewLine}",
            ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:MintAPI.Tests"] = "Debug"
        });
        try
        {
            using var activity = new Activity("configuration-test").SetIdFormat(ActivityIdFormat.W3C).Start();
            using (var logger = (Serilog.Core.Logger)OperationalConfiguration.CreateLogger(config, new StoragePaths(config)))
            {
                logger.Information("hidden");
                logger.ForContext("SourceContext", "MintAPI.Tests.Probe").Debug("visible");
            }
            var text = File.ReadAllText(Path.Combine(root, "service.log"));
            Assert.Contains(activity.TraceId + "|visible", text);
            Assert.DoesNotContain("hidden", text);
            config["LogFiles:Enabled"] = "false";
            using (var logger = (Serilog.Core.Logger)OperationalConfiguration.CreateLogger(config, new StoragePaths(config))) logger.Error("disabled");
            Assert.Equal(text, File.ReadAllText(Path.Combine(root, "service.log")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("None", "Information", false, true)]
    [InlineData("Information", "None", true, false)]
    public void None_log_level_respects_more_specific_source_overrides(string defaultLevel, string sourceLevel,
        bool defaultVisible, bool sourceVisible)
    {
        var root = Path.Combine(Path.GetTempPath(), "mint-log-levels-" + Guid.NewGuid());
        var config = Config(new()
        {
            ["Storage:LogDirectory"] = root, ["LogFiles:ConsoleEnabled"] = "false",
            ["LogFiles:FileName"] = "levels.log", ["LogFiles:RollingInterval"] = "Infinite",
            ["Logging:LogLevel:Default"] = defaultLevel, ["Logging:LogLevel:MintAPI.Tests"] = sourceLevel
        });
        try
        {
            using (var logger = (Serilog.Core.Logger)OperationalConfiguration.CreateLogger(config, new StoragePaths(config)))
            {
                logger.Information("general event");
                logger.ForContext("SourceContext", "MintAPI.Tests.Probe").Information("source event");
            }
            var text = File.ReadAllText(Path.Combine(root, "levels.log"));
            Assert.Equal(defaultVisible, text.Contains("general event"));
            Assert.Equal(sourceVisible, text.Contains("source event"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static IConfigurationRoot Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private sealed class Environment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "MintAPI.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
