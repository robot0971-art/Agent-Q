using System.IO;

namespace AgentQ.Desktop.Services;

public static class DesktopVerificationSelector
{
    private static readonly string[] JavaScriptExtensions = [".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".svelte"];

    private static readonly string[] PythonExtensions = [".py", ".pyi"];

    public static IReadOnlyList<AgentVerificationPlan> SelectPlans(
        IReadOnlyList<FileChangeRecord> changes,
        IReadOnlyList<string> executedCommands,
        ProjectMemory? projectMemory = null)
    {
        if (changes.Count == 0)
        {
            return [];
        }

        if (HasVerificationCommand(executedCommands))
        {
            return
            [
                new AgentVerificationPlan
                {
                    Title = "Verification already ran",
                    Reason = "A build or test command was already executed during this run.",
                    AlreadySatisfied = true
                }
            ];
        }

        var paths = changes
            .Select(change => change.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (paths.Any(IsDesktopProjectFile))
        {
            return
            [
                new AgentVerificationPlan
                {
                    Title = "Suggested verification",
                    Command = "New-Item -ItemType Directory -Force .agentq-verify\\desktop-out | Out-Null; dotnet build csharp\\AgentQ.Desktop\\AgentQ.Desktop.csproj --no-restore -p:OutDir=.agentq-verify\\desktop-out\\",
                    Reason = "Desktop UI or service files changed; build the WPF project in an isolated output folder before trying the app."
                }
            ];
        }

        if (paths.Any(IsCSharpProjectFile))
        {
            var buildCommand = projectMemory?.VerificationCommands
                .FirstOrDefault(command => command.Contains("build.cmd", StringComparison.OrdinalIgnoreCase));
            var testCommand = projectMemory?.VerificationCommands
                .FirstOrDefault(command => command.Contains("test.cmd", StringComparison.OrdinalIgnoreCase));

            return
            [
                new AgentVerificationPlan
                {
                    Title = "Suggested verification",
                    Command = string.IsNullOrWhiteSpace(buildCommand) ? "cmd /c build.cmd" : buildCommand,
                    Reason = "C# source or project files changed."
                },
                new AgentVerificationPlan
                {
                    Title = "Suggested verification",
                    Command = string.IsNullOrWhiteSpace(testCommand) ? "cmd /c test.cmd" : testCommand,
                    Reason = "C# behavior may have changed; run the non-integration test suite."
                }
            ];
        }

        if (paths.Any(IsDockerComposeFile))
        {
            return
            [
                new AgentVerificationPlan
                {
                    Title = "Focused verification",
                    Command = "docker compose config",
                    Reason = "Docker Compose files changed; validate the compose graph before running containers."
                }
            ];
        }

        if (paths.Any(IsJavaScriptProjectFile))
        {
            var packageRoot = FindNearestProjectRoot(paths, IsJavaScriptProjectFile, "package.json");
            var command = FindPreferredCommand(
                projectMemory,
                ["npm run build", "npm test", "pnpm build", "pnpm test", "yarn build", "yarn test"]) ??
                BuildDirectoryCommand(packageRoot, "npm run build");

            return
            [
                new AgentVerificationPlan
                {
                    Title = "Focused verification",
                    Command = command,
                    Reason = "JavaScript or TypeScript files changed; run the nearest package build/test check."
                }
            ];
        }

        if (paths.Any(IsPythonProjectFile))
        {
            var pythonRoot = FindNearestProjectRoot(paths, IsPythonProjectFile, "pyproject.toml", "requirements.txt", "setup.py", "pytest.ini");
            var command = FindPreferredCommand(
                projectMemory,
                ["python -m pytest", "pytest"]) ??
                BuildDirectoryCommand(pythonRoot, "python -m pytest");

            return
            [
                new AgentVerificationPlan
                {
                    Title = "Focused verification",
                    Command = command,
                    Reason = "Python files changed; run the nearest pytest check."
                }
            ];
        }

        if (paths.Any(IsDocumentationFile))
        {
            return
            [
                new AgentVerificationPlan
                {
                    Title = "Manual review suggested",
                    Reason = "Only documentation-style files changed; automated build/test is optional."
                }
            ];
        }

        return
        [
            new AgentVerificationPlan
            {
                Title = "Manual verification suggested",
                Reason = "No project-specific automated verification rule matched these file changes."
            }
        ];
    }

    private static bool HasVerificationCommand(IReadOnlyList<string> commands)
    {
        return commands.Any(command =>
        {
            var normalized = command.Replace('/', '\\').ToLowerInvariant();
            return normalized.Contains("test.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("build.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("build.desktop.cmd", StringComparison.Ordinal) ||
                   normalized.Contains("dotnet test", StringComparison.Ordinal) ||
                   normalized.Contains("dotnet build", StringComparison.Ordinal) ||
                   normalized.Contains("npm test", StringComparison.Ordinal) ||
                   normalized.Contains("npm run build", StringComparison.Ordinal) ||
                   normalized.Contains("pnpm test", StringComparison.Ordinal) ||
                   normalized.Contains("pnpm build", StringComparison.Ordinal) ||
                   normalized.Contains("yarn test", StringComparison.Ordinal) ||
                   normalized.Contains("yarn build", StringComparison.Ordinal) ||
                   normalized.Contains("python -m pytest", StringComparison.Ordinal) ||
                   normalized.Contains("pytest", StringComparison.Ordinal) ||
                   normalized.Contains("docker compose config", StringComparison.Ordinal);
        });
    }

    private static bool IsDesktopProjectFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("csharp/AgentQ.Desktop/", StringComparison.OrdinalIgnoreCase) &&
               (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCSharpProjectFile(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDocumentationFile(string path)
    {
        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".rst", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDockerComposeFile(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        return fileName.Equals("docker-compose.yml", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("docker-compose.yaml", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("compose.yml", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJavaScriptProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return JavaScriptExtensions.Any(value => value.Equals(extension, StringComparison.OrdinalIgnoreCase)) ||
               Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("yarn.lock", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPythonProjectFile(string path)
    {
        var extension = Path.GetExtension(path);
        return PythonExtensions.Any(value => value.Equals(extension, StringComparison.OrdinalIgnoreCase)) ||
               Path.GetFileName(path).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("setup.py", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Equals("pytest.ini", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindPreferredCommand(ProjectMemory? projectMemory, IReadOnlyList<string> needles)
    {
        if (projectMemory == null)
        {
            return null;
        }

        return projectMemory.VerificationCommands
            .Concat(projectMemory.ContextBank.KeyCommands.Select(fact => fact.Value))
            .FirstOrDefault(command => needles.Any(needle => command.Contains(needle, StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildDirectoryCommand(string? directory, string command)
    {
        return string.IsNullOrWhiteSpace(directory)
            ? command
            : $"cmd /c cd {directory} && {command}";
    }

    private static string? FindNearestProjectRoot(
        IReadOnlyList<string> paths,
        Func<string, bool> predicate,
        params string[] markerFileNames)
    {
        var candidate = paths.FirstOrDefault(predicate);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var segments = candidate.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var fileName = segments[^1];
        if (markerFileNames.Any(marker => marker.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return segments.Length <= 1 ? null : string.Join('/', segments.Take(segments.Length - 1));
        }

        return segments.Length <= 1 ? null : segments[0];
    }
}
