using System.IO;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static class ToolPermissionClassifier
{
    private static readonly Regex DestructiveCommandPattern = new(
        @"\b(remove-item|rm|del|erase|rmdir|rd)\b(?=.*(?:-r\b|-recurse\b|/s\b))(?=.*(?:-fo\b|-force\b|/q\b|/f\b))|\bgit\s+reset\s+--hard\b|\bgit\s+clean\s+-fd\b|\bshutdown\b|\breboot\b|\bformat\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ToolPermissionAssessment Assess(string toolName, string inputJson)
    {
        var input = DesktopToolInputParser.Parse(inputJson);
        return Assess(toolName, input);
    }

    public static ToolPermissionAssessment Assess(string toolName, IReadOnlyDictionary<string, object?> input)
    {
        return toolName switch
        {
            "write_file" or "edit_file" => AssessFileMutation(toolName, input),
            "bash" => AssessShell(input),
            _ => new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = toolName,
                Reason = "This tool can affect the local workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessFileMutation(string toolName, IReadOnlyDictionary<string, object?> input)
    {
        var path = input.TryGetValue("path", out var rawPath) ? rawPath as string : null;
        var risk = IsWorkspacePath(path)
            ? PermissionRiskLevel.ProjectWrite
            : PermissionRiskLevel.ExternalWrite;

        return new ToolPermissionAssessment
        {
            RiskLevel = risk,
            Operation = toolName == "write_file" ? "Write file" : "Edit file",
            Target = path ?? "(missing path)",
            Reason = risk == PermissionRiskLevel.ProjectWrite
                ? "This will modify a file inside the selected workspace."
                : "This may modify a file outside the selected workspace."
        };
    }

    private static ToolPermissionAssessment AssessShell(IReadOnlyDictionary<string, object?> input)
    {
        var command = input.TryGetValue("command", out var rawCommand) ? rawCommand as string : null;
        command = command?.Trim() ?? string.Empty;
        var lowered = command.ToLowerInvariant();

        if (DestructiveCommandPattern.IsMatch(command))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.Destructive,
                Operation = "Destructive shell command",
                Target = command,
                Reason = "This command matches a destructive command pattern and is blocked by default."
            };
        }

        if (lowered.Contains("git commit", StringComparison.Ordinal) ||
            lowered.Contains("git push", StringComparison.Ordinal) ||
            lowered.Contains("git tag", StringComparison.Ordinal))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.GitWrite,
                Operation = "Git write command",
                Target = command,
                Reason = "This command can change local or remote Git history/state."
            };
        }

        if (IsVerificationCommand(lowered))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.VerificationCommand,
                Operation = "Verification command",
                Target = command,
                Reason = "This appears to build or test the selected project."
            };
        }

        if (IsNetworkCommand(lowered))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.Network,
                Operation = "Network command",
                Target = command,
                Reason = "This command may access the network or install dependencies."
            };
        }

        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ShellCommand,
            Operation = "Shell command",
            Target = command,
            Reason = "This command will run in a local shell."
        };
    }

    private static bool IsWorkspacePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var workspaceRoot = Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return !Path.IsPathRooted(path);
        }

        try
        {
            var root = Path.GetFullPath(workspaceRoot);
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVerificationCommand(string command)
    {
        return command.Contains("test.cmd", StringComparison.Ordinal) ||
               command.Contains("build.cmd", StringComparison.Ordinal) ||
               command.Contains("build.desktop.cmd", StringComparison.Ordinal) ||
               command.Contains("dotnet test", StringComparison.Ordinal) ||
               command.Contains("dotnet build", StringComparison.Ordinal);
    }

    private static bool IsNetworkCommand(string command)
    {
        return command.Contains("git fetch", StringComparison.Ordinal) ||
               command.Contains("git pull", StringComparison.Ordinal) ||
               command.Contains("curl", StringComparison.Ordinal) ||
               command.Contains("invoke-webrequest", StringComparison.Ordinal) ||
               command.Contains("iwr", StringComparison.Ordinal) ||
               command.Contains("npm install", StringComparison.Ordinal) ||
               command.Contains("dotnet restore", StringComparison.Ordinal);
    }
}
