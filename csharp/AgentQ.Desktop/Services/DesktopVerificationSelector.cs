namespace AgentQ.Desktop.Services;

public static class DesktopVerificationSelector
{
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
                   normalized.Contains("dotnet build", StringComparison.Ordinal);
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
}
