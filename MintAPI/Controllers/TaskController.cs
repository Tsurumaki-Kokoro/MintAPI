using MintAPI.Configuration;
using MintAPI.Errors;
using Microsoft.AspNetCore.Mvc;
using MintAPI.Services;
using MintAPI.Services.Preview;

namespace MintAPI.Controllers;

/// <summary>运维任务：清缓存、打包日志。</summary>
[ApiController]
[Route("task")]
public class TaskController(
    IBeatmapPreviewService preview,
    ILogger<TaskController> logger, StoragePaths paths) : ControllerBase
{
    /// <summary>清空谱面 osu! 文件、预览产物、谱包与下载的头像、Banner、徽章缓存；保留用户上传的背景。</summary>
    /// <response code="200">清理完成。</response>
    /// <response code="500">清理过程中出错。</response>
    [HttpPost("clear_cache")]
    [Produces("application/json", "application/problem+json")]
    public async Task<IActionResult> ClearCache()
    {
        var cacheDir = paths.CacheDirectory;
        try
        {
            await preview.ClearCacheAsync(HttpContext.RequestAborted);
            var beatmapCacheDir = Path.Combine(cacheDir, "beatmap", "osu_file");
            if (Directory.Exists(beatmapCacheDir))
            {
                foreach (var dir in Directory.GetDirectories(beatmapCacheDir))
                    Directory.Delete(dir, recursive: true);
            }

            // User-uploaded backgrounds are persistent customizations, not disposable downloads.
            foreach (var name in new[] { "avatar", "user_banner", "badge" })
            {
                var directory = Path.Combine(cacheDir, name);
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            var userCacheDir = Path.Combine(cacheDir, "user");
            if (Directory.Exists(userCacheDir))
            {
                foreach (var userDir in Directory.GetDirectories(userCacheDir))
                {
                    var dirName = Path.GetFileName(userDir);
                    foreach (var file in Directory.GetFiles(userDir))
                    {
                        if (Path.GetFileNameWithoutExtension(file) == dirName)
                            System.IO.File.Delete(file);
                    }
                    if (!Directory.EnumerateFileSystemEntries(userDir).Any())
                        Directory.Delete(userDir);
                }
            }
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to clear cache");
            return ApiErrors.Result(ErrorCatalog.InternalError, diagnostic: ex.Message);
        }

        return Ok(new { message = "Cache cleared" });
    }

    /// <summary>把日志目录打包成 zip 下载。</summary>
    /// <response code="200">logs.zip。</response>
    /// <response code="404">没有日志目录。</response>
    /// <response code="500">打包失败。</response>
    [HttpPost("pack_logs")]
    [Produces("application/zip", "application/problem+json")]
    public IActionResult PackLogs()
    {
        var logDir = paths.LogDirectory;
        if (!Directory.Exists(logDir))
            return ApiErrors.Result(ErrorCatalog.RecordNotFound, diagnostic: "No logs directory found");

        var zipPath = Path.Combine(Path.GetTempPath(), $"mintapi-logs-{Guid.NewGuid():N}.zip");
        try
        {
            System.IO.Compression.ZipFile.CreateFromDirectory(logDir, zipPath);
            var bytes = System.IO.File.ReadAllBytes(zipPath);
            System.IO.File.Delete(zipPath);
            return File(bytes, "application/zip", "logs.zip");
        }
        catch (Exception ex) when (ex is not RetryableException && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to pack logs");
            return ApiErrors.Result(ErrorCatalog.InternalError, diagnostic: ex.Message);
        }
        finally
        {
            if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
        }
    }
}
