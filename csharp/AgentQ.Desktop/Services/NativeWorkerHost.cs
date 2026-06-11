using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class NativeWorkerHost
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<NativeWorkerResult?> AnalyzeAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var scriptPath = ResolveWorkerScriptPath();
        if (scriptPath == null || string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(Path.GetFullPath(workspaceRoot));
            using var process = Process.Start(startInfo);

            if (process == null)
            {
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return new NativeWorkerResult
                {
                    Worker = "native-worker",
                    Root = workspaceRoot,
                    Warnings = [string.IsNullOrWhiteSpace(stderr) ? $"Worker exited with {process.ExitCode:0}." : stderr.Trim()]
                };
            }

            return JsonSerializer.Deserialize<NativeWorkerResult>(stdout, Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TaskCanceledException)
        {
            return new NativeWorkerResult
            {
                Worker = "native-worker",
                Root = workspaceRoot,
                Warnings = [$"Worker unavailable: {ex.Message}"]
            };
        }
    }

    private static string? ResolveWorkerScriptPath()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            var candidate = Path.Combine(current, "tools", "language-workers", "native-worker.mjs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }
}
