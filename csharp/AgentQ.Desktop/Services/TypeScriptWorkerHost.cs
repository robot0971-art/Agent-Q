using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class TypeScriptWorkerHost
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TypeScriptWorkerResult?> AnalyzeAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var scriptPath = ResolveWorkerScriptPath();
        if (scriptPath == null || string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\" \"{Path.GetFullPath(workspaceRoot)}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

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
                return new TypeScriptWorkerResult
                {
                    Worker = "typescript-worker",
                    Root = workspaceRoot,
                    Warnings = [string.IsNullOrWhiteSpace(stderr) ? $"Worker exited with {process.ExitCode:0}." : stderr.Trim()]
                };
            }

            return JsonSerializer.Deserialize<TypeScriptWorkerResult>(stdout, Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TaskCanceledException)
        {
            return new TypeScriptWorkerResult
            {
                Worker = "typescript-worker",
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
            var candidate = Path.Combine(current, "tools", "language-workers", "typescript-worker.mjs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        var workspaceCandidate = Path.Combine(Environment.CurrentDirectory, "tools", "language-workers", "typescript-worker.mjs");
        return File.Exists(workspaceCandidate) ? workspaceCandidate : null;
    }
}
