namespace MintAPI.Configuration;

/// <summary>Resolves mutable storage paths once, against the application content root.</summary>
public sealed class StoragePaths
{
    public string CacheDirectory { get; }
    public string LogDirectory { get; }
    public string TokenCacheDirectory { get; }

    public StoragePaths(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        var root = Path.GetFullPath(environment?.ContentRootPath ?? Directory.GetCurrentDirectory());
        var storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new();
        if (!storage.IsValid()) throw new ArgumentException("Invalid Storage paths.");
        var cache = configuration["CacheDir"] ?? "cache";
        if (string.IsNullOrWhiteSpace(cache)) throw new ArgumentException("CacheDir must not be empty.");
        CacheDirectory = Path.GetFullPath(cache, root);
        LogDirectory = Path.GetFullPath(storage.LogDirectory, root);
        TokenCacheDirectory = Path.GetFullPath(storage.TokenCacheDirectory ?? Path.Combine(AppContext.BaseDirectory, "token_cache"), root);
        // Clearing a cache must never remove logs or credential storage.
        if (Contains(CacheDirectory, LogDirectory) || Contains(LogDirectory, CacheDirectory) ||
            Contains(CacheDirectory, TokenCacheDirectory) || Contains(TokenCacheDirectory, CacheDirectory) ||
            Contains(LogDirectory, TokenCacheDirectory) || Contains(TokenCacheDirectory, LogDirectory))
            throw new ArgumentException("Cache, log and token directories must not overlap.");
    }

    private static bool Contains(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative == "." || !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
