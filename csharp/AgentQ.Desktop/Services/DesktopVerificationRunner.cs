using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationRunner(IEnumerable<IVerificationArtifactCollector> artifactCollectors)
{
    public async Task<VerificationRunResult> RunAsync(
        AgentVerificationPlan plan,
        string workingDirectory,
        TimeSpan timeout,
        IEnumerable<string>? projectAllowedCommands = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plan.Command))
        {
            throw new InvalidOperationException("Verification plan does not have a runnable command.");
        }

        if (!VerificationCommandPolicy.IsAllowed(plan.Command, projectAllowedCommands))
        {
            throw new InvalidOperationException("The command is not in the verification allowlist.");
        }

        try
        {
            var result = await RunPowerShellCommandAsync(plan.Command, workingDirectory, timeout, ct);
            return AttachArtifacts(plan, result, workingDirectory);
        }
        finally
        {
            TryDeleteVerificationOutput(workingDirectory);
        }
    }

    private static async Task<VerificationRunResult> RunPowerShellCommandAsync(
        string command,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw new TimeoutException($"Verification timed out after {timeout.TotalSeconds:0} seconds.");
        }

        return new VerificationRunResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = await stdoutTask,
            StandardError = await stderrTask
        };
    }

    private VerificationRunResult AttachArtifacts(
        AgentVerificationPlan plan,
        VerificationRunResult result,
        string workingDirectory)
    {
        var artifacts = artifactCollectors
            .SelectMany(collector => collector.Collect(plan, result, workingDirectory))
            .GroupBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return artifacts.Count == 0
            ? result
            : new VerificationRunResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError,
                Artifacts = artifacts
            };
    }

    private static void TryDeleteVerificationOutput(string workingDirectory)
    {
        try
        {
            var root = Path.GetFullPath(workingDirectory);
            var target = Path.GetFullPath(Path.Combine(root, ".agentq-verify"));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(target))
            {
                var targetInfo = new DirectoryInfo(target);
                if ((targetInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    !WorkspacePathResolver.IsResolvedInsideWorkspace(root, target))
                {
                    return;
                }

                Directory.Delete(target, recursive: true);
            }
        }
        catch
        {
            // Verification output cleanup is best-effort.
        }
    }
}
