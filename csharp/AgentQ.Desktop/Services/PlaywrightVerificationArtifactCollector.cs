using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class PlaywrightVerificationArtifactCollector : IVerificationArtifactCollector
{
    private static readonly string[] ReportDirectories = ["playwright-report", "test-results"];

    private static readonly string[] ScreenshotExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public IReadOnlyList<VerificationArtifact> Collect(
        AgentVerificationPlan plan,
        VerificationRunResult result,
        string workspaceRoot)
    {
        if (!IsPlaywrightPlan(plan, result) || !Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var artifacts = new List<VerificationArtifact>();
        var commandDirectory = ResolveCommandDirectory(plan.Command, workspaceRoot);
        foreach (var root in EnumerateCandidateRoots(workspaceRoot, commandDirectory))
        {
            CollectReportDirectories(root, workspaceRoot, artifacts);
        }

        return artifacts
            .GroupBy(artifact => artifact.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(12)
            .ToList();
    }

    private static bool IsPlaywrightPlan(AgentVerificationPlan plan, VerificationRunResult result)
    {
        var text = string.Join(' ', plan.Command, plan.Reason, result.CombinedOutput);
        return text.Contains("playwright", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("test:e2e", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string workspaceRoot, string? commandDirectory)
    {
        yield return workspaceRoot;

        if (!string.IsNullOrWhiteSpace(commandDirectory) &&
            !commandDirectory.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            yield return commandDirectory;
        }
    }

    private static void CollectReportDirectories(
        string searchRoot,
        string workspaceRoot,
        List<VerificationArtifact> artifacts)
    {
        foreach (var reportDirectory in ReportDirectories)
        {
            var directory = Path.Combine(searchRoot, reportDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            artifacts.Add(new VerificationArtifact
            {
                Kind = "playwright-report",
                Path = ToRelativePath(workspaceRoot, directory),
                Description = reportDirectory.Equals("playwright-report", StringComparison.OrdinalIgnoreCase)
                    ? "Playwright HTML report directory."
                    : "Playwright test output directory."
            });

            foreach (var screenshot in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                         .Where(IsScreenshot)
                         .Take(6))
            {
                artifacts.Add(new VerificationArtifact
                {
                    Kind = "screenshot",
                    Path = ToRelativePath(workspaceRoot, screenshot),
                    Description = "Playwright screenshot evidence."
                });
            }
        }
    }

    private static string? ResolveCommandDirectory(string? command, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        var prefix = trimmed.StartsWith("cmd.exe /c cd ", StringComparison.OrdinalIgnoreCase)
            ? "cmd.exe /c cd "
            : trimmed.StartsWith("cmd /c cd ", StringComparison.OrdinalIgnoreCase)
                ? "cmd /c cd "
                : string.Empty;
        if (prefix.Length == 0)
        {
            return null;
        }

        var separatorIndex = trimmed.IndexOf("&&", StringComparison.Ordinal);
        if (separatorIndex <= prefix.Length)
        {
            return null;
        }

        var directory = trimmed[prefix.Length..separatorIndex].Trim();
        if (directory.StartsWith("/d ", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory[3..].Trim();
        }

        directory = Unquote(directory);
        if (string.IsNullOrWhiteSpace(directory) || Path.IsPathRooted(directory))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, directory));
        var root = Path.GetFullPath(workspaceRoot);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1].Trim();
        }

        return value;
    }

    private static bool IsScreenshot(string path)
    {
        var extension = Path.GetExtension(path);
        return ScreenshotExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelativePath(string workspaceRoot, string path)
    {
        return Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/');
    }
}
