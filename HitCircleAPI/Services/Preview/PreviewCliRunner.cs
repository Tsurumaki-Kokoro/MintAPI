using System.Diagnostics;

namespace HitCircleAPI.Services.Preview;

public record PreviewCliOutput(int ExitCode, string StandardOutput, string StandardError);

public interface IPreviewCliRunner
{
    Task<PreviewCliOutput> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class PreviewCliRunner : IPreviewCliRunner
{
    public async Task<PreviewCliOutput> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new PreviewFailedException($"Cannot start preview CLI: {ex.Message}");
        }
        // Drain both pipes concurrently, including while terminating the process.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The process can exit between HasExited and Kill.
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        return new PreviewCliOutput(process.ExitCode, await stdout, await stderr);
    }
}
