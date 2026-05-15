using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationRunner
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
            return await RunPowerShellCommandAsync(plan.Command, workingDirectory, timeout, ct);
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
                Directory.Delete(target, recursive: true);
            }
        }
        catch
        {
            // Verification output cleanup is best-effort.
        }
    }
}
