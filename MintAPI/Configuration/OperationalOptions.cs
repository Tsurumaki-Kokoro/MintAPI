namespace MintAPI.Configuration;

public sealed class StorageOptions
{
    public string LogDirectory { get; set; } = "logs";
    public string? TokenCacheDirectory { get; set; }
    public bool IsValid() => !string.IsNullOrWhiteSpace(LogDirectory) && (TokenCacheDirectory is null || !string.IsNullOrWhiteSpace(TokenCacheDirectory));
}

public sealed class DownloadOptions
{
    public int TimeoutSeconds { get; set; } = 100;
    public int BannerTimeoutSeconds { get; set; } = 8;
    public int ListCoverTimeoutSeconds { get; set; } = 5;
    public int SearchCoverTimeoutSeconds { get; set; } = 8;
    public int OsuTrackTimeoutSeconds { get; set; } = 10;
    public long MaxSearchCoverBytes { get; set; } = 4 * 1024 * 1024;
    public string OsuTrackBaseUrl { get; set; } = "https://osutrack-api.ameo.dev/";
    public string[] BeatmapSources { get; set; } = ["https://osu.ppy.sh/osu/{beatmapId}", "https://api.osu.direct/osu/{beatmapId}"];
    public string[] BackgroundSources { get; set; } = ["https://api.osu.direct/media/background/{beatmapId}", "https://subapi.nerinyan.moe/bg/{beatmapId}", "https://dl.sayobot.cn/beatmaps/files/{setId}/{backgroundName}"];
    public string CoverUrlTemplate { get; set; } = "https://assets.ppy.sh/beatmaps/{setId}/covers/cover@2x.jpg";
    public string SeasonalBackgroundsUrl { get; set; } = "https://osu.ppy.sh/api/v2/seasonal-backgrounds";
    public string[] SearchCoverAllowedHosts { get; set; } = ["assets.ppy.sh"];

    public static DownloadOptions Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("Downloads");
        var options = section.Get<DownloadOptions>() ?? new();
        // Binding initialized arrays appends entries; configured source lists must replace defaults.
        options.BeatmapSources = section.GetSection(nameof(BeatmapSources)).Get<string[]>() ?? new DownloadOptions().BeatmapSources;
        options.BackgroundSources = section.GetSection(nameof(BackgroundSources)).Get<string[]>() ?? new DownloadOptions().BackgroundSources;
        options.SearchCoverAllowedHosts = section.GetSection(nameof(SearchCoverAllowedHosts)).Get<string[]>() ?? new DownloadOptions().SearchCoverAllowedHosts;
        return options;
    }

    public static string Expand(string template, int beatmapId = 0, int setId = 0, string backgroundName = "") => template
        .Replace("{beatmapId}", beatmapId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Replace("{setId}", setId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Replace("{backgroundName}", Uri.EscapeDataString(backgroundName));

    private static bool ValidUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    private static bool ValidTemplate(string? value) => value is not null &&
        !Expand(value).Contains('{') && !Expand(value).Contains('}') && ValidUrl(Expand(value));
    public bool IsValid() => TimeoutSeconds is >= 1 and <= 86400 && BannerTimeoutSeconds is >= 1 and <= 86400 && ListCoverTimeoutSeconds is >= 1 and <= 86400 &&
        SearchCoverTimeoutSeconds is >= 1 and <= 86400 && OsuTrackTimeoutSeconds is >= 1 and <= 86400 && MaxSearchCoverBytes is >= 1 and <= 1073741824 &&
        ValidUrl(OsuTrackBaseUrl) && OsuTrackBaseUrl.EndsWith('/') && ValidUrl(SeasonalBackgroundsUrl) &&
        BeatmapSources is { Length: > 0 } && BeatmapSources.All(value => ValidTemplate(value) && value.Contains("{beatmapId}")) &&
        BackgroundSources is { Length: > 0 } && BackgroundSources.All(ValidTemplate) &&
        ValidTemplate(CoverUrlTemplate) && CoverUrlTemplate.Contains("{setId}") &&
        SearchCoverAllowedHosts is { Length: > 0 } && SearchCoverAllowedHosts.All(host => Uri.CheckHostName(host) != UriHostNameType.Unknown);
}

public sealed class CachePolicyOptions
{
    public int AvatarHours { get; set; } = 24;
    public int BannerHours { get; set; } = 24;
    public int SearchSeconds { get; set; } = 120;
    public int SearchCoverMinutes { get; set; } = 30;
    public int OsuTrackMinutes { get; set; } = 10;
    // Zero preserves permanent on-disk caches; positive values enable refresh.
    public int BeatmapFileHours { get; set; }
    public int BackgroundHours { get; set; }
    public int BadgeHours { get; set; }
    public int ListCoverHours { get; set; }
    public int PreviewArtifactHours { get; set; }
    public bool IsValid() => AvatarHours is >= 1 and <= 87600 && BannerHours is >= 1 and <= 87600 && SearchSeconds is >= 1 and <= 315360000 && SearchCoverMinutes is >= 1 and <= 5256000 &&
        OsuTrackMinutes is >= 1 and <= 5256000 && BeatmapFileHours is >= 0 and <= 87600 && BackgroundHours is >= 0 and <= 87600 && BadgeHours is >= 0 and <= 87600 && ListCoverHours is >= 0 and <= 87600 && PreviewArtifactHours is >= 0 and <= 87600;
    public static bool IsFresh(string path, int hours) => File.Exists(path) &&
        (hours == 0 || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(hours));
}

public sealed class ConcurrencyOptions
{
    public int PpCalculation { get; set; } = 4;
    public int CoverDownload { get; set; } = 4;
    public bool IsValid() => PpCalculation is >= 1 and <= 64 && CoverDownload is >= 1 and <= 64;
}

public sealed class LogFileOptions
{
    public bool Enabled { get; set; } = true;
    public bool ConsoleEnabled { get; set; } = true;
    public string FileName { get; set; } = ".log";
    public int RetainedFileCountLimit { get; set; } = 31;
    public long FileSizeLimitBytes { get; set; } = 1073741824;
    public bool RollOnFileSizeLimit { get; set; }
    public string RollingInterval { get; set; } = "Day";
    public string OutputTemplate { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}";
    public bool IsValid() => !string.IsNullOrWhiteSpace(FileName) && FileName is not ("." or "..") &&
        FileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !FileName.Contains('/') && !FileName.Contains('\\') &&
        RetainedFileCountLimit > 0 && FileSizeLimitBytes > 0 && !string.IsNullOrWhiteSpace(OutputTemplate) &&
        Enum.TryParse<Serilog.RollingInterval>(RollingInterval, out var interval) && Enum.IsDefined(interval);
}
