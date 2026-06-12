using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class DesktopGitService
{
    public Task<GitCommandResult> GetStatusAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, BuildPathScopedArguments(["status", "--short", "--branch"]), TimeSpan.FromSeconds(30), ct);
    }

    public Task<GitCommandResult> GetDiffStatAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, BuildPathScopedArguments(["diff", "--stat"]), TimeSpan.FromSeconds(30), ct);
    }

    public Task<GitCommandResult> GetFullDiffAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, BuildPathScopedArguments(["diff", "HEAD"]), TimeSpan.FromSeconds(30), ct);
    }

    public async Task<IReadOnlyList<GitChangedFile>> GetChangedFilesAsync(
        string workingDirectory,
        CancellationToken ct = default)
    {
        var result = await RunGitAsync(
            workingDirectory,
            BuildPathScopedArguments(["status", "--porcelain=v1", "--untracked-files=normal"]),
            TimeSpan.FromSeconds(30),
            ct);

        if (!result.Succeeded)
        {
            return [];
        }

        return ParseChangedFiles(result.StandardOutput);
    }

    public Task<GitCommandResult> GetFileDiffAsync(
        string workingDirectory,
        GitChangedFile file,
        CancellationToken ct = default)
    {
        if (IsBlockedAgentMetadataPath(file))
        {
            return Task.FromResult(BlockedAgentMetadataResult());
        }

        if (file.Status.Contains("??", StringComparison.Ordinal))
        {
            return Task.FromResult(new GitCommandResult
            {
                ExitCode = 0,
                StandardOutput = "Untracked file. There is no git diff yet."
            });
        }

        return RunGitAsync(
            workingDirectory,
            ["diff", "HEAD", "--", file.Path],
            TimeSpan.FromSeconds(30),
            ct);
    }

    public Task<GitCommandResult> StageFileAsync(
        string workingDirectory,
        GitChangedFile file,
        CancellationToken ct = default)
    {
        if (IsBlockedAgentMetadataPath(file))
        {
            return Task.FromResult(BlockedAgentMetadataResult());
        }

        return RunGitAsync(
            workingDirectory,
            ["add", "--", file.Path],
            TimeSpan.FromSeconds(30),
            ct);
    }

    public async Task<GitCommandResult> StageFilesAsync(
        string workingDirectory,
        IReadOnlyList<GitChangedFile> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            return new GitCommandResult
            {
                ExitCode = 1,
                StandardError = "No files selected to stage."
            };
        }

        if (files.Any(IsBlockedAgentMetadataPath))
        {
            return BlockedAgentMetadataResult();
        }

        var arguments = new List<string> { "add", "--" };
        arguments.AddRange(files.Select(file => file.Path));
        return await RunGitAsync(workingDirectory, arguments, TimeSpan.FromSeconds(30), ct);
    }

    public Task<GitCommandResult> UnstageFileAsync(
        string workingDirectory,
        GitChangedFile file,
        CancellationToken ct = default)
    {
        if (IsBlockedAgentMetadataPath(file))
        {
            return Task.FromResult(BlockedAgentMetadataResult());
        }

        return RunGitAsync(
            workingDirectory,
            ["restore", "--staged", "--", file.Path],
            TimeSpan.FromSeconds(30),
            ct);
    }

    public Task<GitCommandResult> CommitAsync(
        string workingDirectory,
        string message,
        CancellationToken ct = default)
    {
        return RunGitAsync(
            workingDirectory,
            ["commit", "-m", message],
            TimeSpan.FromSeconds(60),
            ct);
    }

    public Task<GitCommandResult> PullFastForwardOnlyAsync(
        string workingDirectory,
        CancellationToken ct = default)
    {
        return RunGitAsync(
            workingDirectory,
            ["pull", "--ff-only"],
            TimeSpan.FromSeconds(60),
            ct);
    }

    public Task<GitCommandResult> CreateBranchAsync(
        string workingDirectory,
        string branchName,
        CancellationToken ct = default)
    {
        return RunGitAsync(
            workingDirectory,
            ["branch", branchName],
            TimeSpan.FromSeconds(30),
            ct);
    }

    public Task<GitCommandResult> CheckoutBranchAsync(
        string workingDirectory,
        string branchName,
        CancellationToken ct = default)
    {
        return RunGitAsync(
            workingDirectory,
            ["checkout", branchName],
            TimeSpan.FromSeconds(30),
            ct);
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!Directory.Exists(workingDirectory))
        {
            return new GitCommandResult
            {
                ExitCode = 1,
                StandardError = "Workspace folder does not exist."
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
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

                return new GitCommandResult
                {
                    ExitCode = 124,
                    StandardError = $"git timed out after {timeout.TotalSeconds:0} seconds."
                };
            }

            return new GitCommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await stdoutTask,
                StandardError = await stderrTask
            };
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new GitCommandResult
            {
                ExitCode = 1,
                StandardError = $"Unable to run git: {ex.Message}"
            };
        }
    }

    private static IReadOnlyList<GitChangedFile> ParseChangedFiles(string statusOutput)
    {
        var files = new List<GitChangedFile>();
        foreach (var rawLine in statusOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length < 4)
            {
                continue;
            }

            var status = rawLine[..2];
            var path = rawLine[3..].Trim();
            string? originalPath = null;

            const string renameMarker = " -> ";
            var renameIndex = path.IndexOf(renameMarker, StringComparison.Ordinal);
            if (renameIndex >= 0)
            {
                originalPath = path[..renameIndex];
                path = path[(renameIndex + renameMarker.Length)..];
            }

            if (IsAgentQInternalPath(path) ||
                (originalPath != null && IsAgentQInternalPath(originalPath)))
            {
                continue;
            }

            files.Add(new GitChangedFile
            {
                Status = status,
                Path = path,
                OriginalPath = originalPath
            });
        }

        return files;
    }

    private static bool IsAgentQInternalPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.Equals(".agentq", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".agentq/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".agents", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".agents/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".codex", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".codex/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(".codex-build", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".codex-build/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedAgentMetadataPath(GitChangedFile file) =>
        IsAgentQInternalPath(file.Path) ||
        (!string.IsNullOrWhiteSpace(file.OriginalPath) && IsAgentQInternalPath(file.OriginalPath));

    private static GitCommandResult BlockedAgentMetadataResult() => new()
    {
        ExitCode = 1,
        StandardError = "AgentQ internal metadata paths cannot be staged, unstaged, or diffed from the git panel."
    };

    private static IReadOnlyList<string> BuildPathScopedArguments(IReadOnlyList<string> prefix)
    {
        var arguments = new List<string>(prefix)
        {
            "--",
            ".",
            ":(exclude).agentq",
            ":(exclude).agents",
            ":(exclude).codex",
            ":(exclude).codex-build"
        };
        return arguments;
    }
}
