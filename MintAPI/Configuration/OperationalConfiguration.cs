using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace MintAPI.Configuration;

public static class OperationalConfiguration
{
    public static IServiceCollection AddOperationalConfiguration(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton(new StoragePaths(configuration, environment));
        services.AddOptions<DownloadOptions>().Bind(configuration.GetSection("Downloads"))
            .PostConfigure(value =>
            {
                var sources = DownloadOptions.Read(configuration);
                value.BeatmapSources = sources.BeatmapSources;
                value.BackgroundSources = sources.BackgroundSources;
                value.SearchCoverAllowedHosts = sources.SearchCoverAllowedHosts;
            })
            .Validate(value => value.IsValid(), "Invalid Downloads timeouts, limits or HTTPS source templates.").ValidateOnStart();
        services.AddOptions<CachePolicyOptions>().Bind(configuration.GetSection("CachePolicy"))
            .Validate(value => value.IsValid(), "Invalid CachePolicy expiration values.").ValidateOnStart();
        services.AddOptions<ConcurrencyOptions>().Bind(configuration.GetSection("Concurrency"))
            .Validate(value => value.IsValid(), "Concurrency must be between 1 and 64.").ValidateOnStart();
        services.AddOptions<LogFileOptions>().Bind(configuration.GetSection("LogFiles"))
            .Validate(value => value.IsValid(), "Invalid LogFiles policy.").ValidateOnStart();
        return services;
    }

    public static Serilog.ILogger CreateLogger(IConfiguration configuration, StoragePaths paths)
    {
        var files = configuration.GetSection("LogFiles").Get<LogFileOptions>() ?? new();
        if (!files.IsValid()) throw new OptionsValidationException("LogFiles", typeof(LogFileOptions), ["Invalid LogFiles policy."]);
        var logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).Enrich.FromLogContext();
        var levels = configuration.GetSection("Logging:LogLevel").GetChildren().ToDictionary(
            level => level.Key, level => level.Value?.ToLowerInvariant(), StringComparer.Ordinal);
        foreach (var (source, level) in levels)
        {
            var severity = level switch
            {
                "trace" or "none" => LogEventLevel.Verbose, "debug" => LogEventLevel.Debug,
                "information" => LogEventLevel.Information, "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error, "critical" => LogEventLevel.Fatal,
                _ => throw new ArgumentException($"Invalid logging level for {source}.")
            };
            if (source == "Default") logger.MinimumLevel.Is(severity);
            else logger.MinimumLevel.Override(source, severity);
        }
        if (levels.Values.Contains("none"))
        {
            logger.Filter.ByExcluding(entry =>
            {
                var source = entry.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string name } ? name : "";
                var match = levels.Where(rule => rule.Key != "Default" &&
                    (source == rule.Key || source.StartsWith(rule.Key + ".", StringComparison.Ordinal)))
                    .OrderByDescending(rule => rule.Key.Length).FirstOrDefault();
                return (match.Key is null ? levels.GetValueOrDefault("Default") : match.Value) == "none";
            });
        }
        if (files.ConsoleEnabled) logger.WriteTo.Console();
        if (files.Enabled) logger.WriteTo.File(Path.Combine(paths.LogDirectory, files.FileName),
            rollingInterval: Enum.Parse<RollingInterval>(files.RollingInterval),
            retainedFileCountLimit: files.RetainedFileCountLimit, fileSizeLimitBytes: files.FileSizeLimitBytes,
            rollOnFileSizeLimit: files.RollOnFileSizeLimit, outputTemplate: files.OutputTemplate);
        return logger.CreateLogger();
    }
}
