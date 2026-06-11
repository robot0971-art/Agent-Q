using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace AgentQ.Desktop.Services;

public sealed class PythonWorkerHost
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PythonWorkerResult?> AnalyzeAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var scriptPath = ResolveWorkerScriptPath();
        if (scriptPath == null || string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return null;
        }

        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        return await TryRunAsync("python", [scriptPath, fullWorkspaceRoot], workspaceRoot, ct) ??
            await TryRunAsync("py", ["-3", scriptPath, fullWorkspaceRoot], workspaceRoot, ct);
    }

    private static async Task<PythonWorkerResult?> TryRunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workspaceRoot,
        CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

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
                return new PythonWorkerResult
                {
                    Worker = "python-worker",
                    Root = workspaceRoot,
                    Warnings = [string.IsNullOrWhiteSpace(stderr) ? $"{fileName} worker exited with {process.ExitCode:0}." : stderr.Trim()]
                };
            }

            return JsonSerializer.Deserialize<PythonWorkerResult>(stdout, Options);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TaskCanceledException or Win32Exception)
        {
            return null;
        }
    }

    private static string? ResolveWorkerScriptPath()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            var candidate = Path.Combine(current, "tools", "language-workers", "python-worker.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }
}
