using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AgentQ.Desktop.Services;

public sealed class DesktopGitService
{
    public Task<GitCommandResult> GetStatusAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, ["status", "--short", "--branch"], TimeSpan.FromSeconds(30), ct);
    }

    public Task<GitCommandResult> GetDiffStatAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, ["diff", "--stat"], TimeSpan.FromSeconds(30), ct);
    }

    public Task<GitCommandResult> GetFullDiffAsync(string workingDirectory, CancellationToken ct = default)
    {
        return RunGitAsync(workingDirectory, ["diff", "HEAD", "--"], TimeSpan.FromSeconds(30), ct);
    }

    public async Task<IReadOnlyList<GitChangedFile>> GetChangedFilesAsync(
        string workingDirectory,
        CancellationToken ct = default)
    {
        var result = await RunGitAsync(
            workingDirectory,
            ["status", "--porcelain=v1", "--untracked-files=normal"],
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

            files.Add(new GitChangedFile
            {
                Status = status,
                Path = path,
                OriginalPath = originalPath
            });
        }

        return files;
    }
}
