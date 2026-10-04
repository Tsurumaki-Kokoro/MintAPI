using Microsoft.Extensions.Options;

namespace MintAPI.Services.Preview;

public sealed class PreviewCliWarmupService(IOptions<BeatmapPreviewOptions> options,
    IPreviewCliRunner runner, ILogger<PreviewCliWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.ExecutablePath;
        if (OperatingSystem.IsWindows() && !Path.HasExtension(path)) path += ".exe";
        path = Path.GetFullPath(path, AppContext.BaseDirectory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (!File.Exists(path)) throw new PreviewFailedException("Preview CLI is not installed.");
            var result = await runner.RunAsync(path, ["--version"], timeout.Token);
            if (result.ExitCode != 0) throw new PreviewFailedException(result.StandardError);
            logger.LogInformation("Preview CLI ready: {Version}", result.StandardOutput.Trim());
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Existing API endpoints remain available when this optional component is absent.
            logger.LogError(ex, "Preview CLI unavailable at {Path}", path);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
