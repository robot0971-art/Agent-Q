using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static class ToolPermissionClassifier
{
    private static readonly Regex[] DestructiveCommandPatterns =
    [
        new(@"\b(remove-item|ri|rm|del|erase)\b(?=.*(?:-r\b|-recurse\b|/s\b))(?=.*(?:-fo\b|-force\b|/q\b|/f\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(rmdir|rd)\b(?=.*(?:-r\b|-recurse\b|/s\b))(?=.*(?:-fo\b|-force\b|/q\b|/f\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+reset\s+--hard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+clean\s+-[a-z]*[fdx][a-z]*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+restore\b(?=.*(?:\s\.|\s:\/|\s--source\b|\s--staged\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgit\s+checkout\s+(?:-f\b|--force\b|--\s+(?:\.|:\/))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(shutdown|reboot|diskpart|format)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(encodedcommand|enc)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(takeown|icacls)\b.*\b(del|erase|rmdir|rd|remove-item|ri|rm)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

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
            "create_project_scaffold" => AssessProjectScaffoldCreation(input),
            "verify_project_scaffold" => AssessProjectScaffoldVerification(input),
            "bash" => AssessShell(input),
            _ => new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = toolName,
                Reason = "This tool can affect the local workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessProjectScaffoldCreation(IReadOnlyDictionary<string, object?> input)
    {
        var files = TryGetPlanStrings(input, "files");
        var commands = TryGetPlanStrings(input, "verificationCommands");
        var overwrite = TryGetBool(input, "overwriteExistingFiles");
        var request = input.TryGetValue("request", out var rawRequest) ? rawRequest as string : null;
        var target = files.Count == 0
            ? string.IsNullOrWhiteSpace(request) ? "(missing approved plan)" : request
            : string.Join(", ", files.Take(8)) + (files.Count > 8 ? $", +{files.Count - 8} more" : string.Empty);
        var commandSummary = commands.Count == 0
            ? "no verification commands"
            : "verification: " + string.Join(", ", commands.Take(3)) + (commands.Count > 3 ? $", +{commands.Count - 3} more" : string.Empty);
        var overwriteSummary = overwrite ? "existing files may be overwritten" : "existing files are not overwritten by default";
        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ProjectWrite,
            Operation = "Create project scaffold",
            Target = target,
            Reason = $"This will create approved scaffold files inside the selected workspace; {commandSummary}; {overwriteSummary}."
        };
    }

    private static ToolPermissionAssessment AssessProjectScaffoldVerification(IReadOnlyDictionary<string, object?> input)
    {
        var commands = TryGetPlanStrings(input, "verificationCommands");
        var command = input.TryGetValue("command", out var rawCommand) ? rawCommand as string : null;
        var selectedCommand = string.IsNullOrWhiteSpace(command) ? commands.FirstOrDefault() : command;
        var target = string.IsNullOrWhiteSpace(selectedCommand)
            ? "(missing plan verification command)"
            : selectedCommand;
        var allowedSummary = commands.Count == 0
            ? "plan contains no verification commands"
            : "plan allows: " + string.Join(", ", commands.Take(3)) + (commands.Count > 3 ? $", +{commands.Count - 3} more" : string.Empty);
        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.VerificationCommand,
            Operation = "Verify project scaffold",
            Target = target,
            Reason = $"This will run a scaffold verification command in the selected workspace; {allowedSummary}."
        };
    }

    private static List<string> TryGetPlanStrings(IReadOnlyDictionary<string, object?> input, string propertyName)
    {
        if (!input.TryGetValue("plan", out var rawPlan) || rawPlan == null)
        {
            return [];
        }

        if (rawPlan is JsonElement element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var jsonArray) &&
            jsonArray.ValueKind == JsonValueKind.Array)
        {
            return jsonArray.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        if (rawPlan is IReadOnlyDictionary<string, object?> dictionary &&
            dictionary.TryGetValue(propertyName, out var rawValues))
        {
            return ExtractStringList(rawValues);
        }

        if (rawPlan is IDictionary<string, object?> mutableDictionary &&
            mutableDictionary.TryGetValue(propertyName, out var rawMutableValues))
        {
            return ExtractStringList(rawMutableValues);
        }

        var property = rawPlan.GetType().GetProperty(propertyName);
        return property == null ? [] : ExtractStringList(property.GetValue(rawPlan));
    }

    private static List<string> ExtractStringList(object? rawValues)
    {
        if (rawValues is IEnumerable<string> strings)
        {
            return strings.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        }

        if (rawValues is IEnumerable<object?> objects)
        {
            return objects
                .Select(value => value?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        return [];
    }

    private static bool TryGetBool(IReadOnlyDictionary<string, object?> input, string key)
    {
        if (!input.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        return raw switch
        {
            bool value => value,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => false
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
        var target = BuildShellCommandTarget(command);

        if (DestructiveCommandPatterns.Any(pattern => pattern.IsMatch(command)))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.Destructive,
                Operation = "Destructive shell command",
                Target = target,
                Reason = "This command matches a destructive shell, Git restore, or system modification pattern and is blocked by default."
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
                Target = target,
                Reason = "This command can change local or remote Git history/state."
            };
        }

        if (IsVerificationCommand(lowered))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.VerificationCommand,
                Operation = "Verification command",
                Target = target,
                Reason = "This appears to build or test the selected project."
            };
        }

        if (IsSafeReadShellCommand(command))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.SafeRead,
                Operation = "Read-only shell command",
                Target = target,
                Reason = "This command only inspects files, directories, or repository state."
            };
        }

        if (IsNetworkCommand(lowered))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.Network,
                Operation = "Network command",
                Target = target,
                Reason = "This command may access the network or install dependencies."
            };
        }

        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ShellCommand,
            Operation = "Shell command",
            Target = target,
            Reason = "This command will run in a local shell."
        };
    }

    public static string BuildShellCommandTarget(string command)
    {
        command = command.Trim();
        var label = ExtractHumanCommandLabel(command);
        if (string.IsNullOrWhiteSpace(label) || string.Equals(label, command, StringComparison.Ordinal))
        {
            return command;
        }

        return $"{label} \u2014 {command}";
    }

    public static string ExtractHumanCommandLabel(string command)
    {
        var normalized = NormalizeShellCommand(command);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var tokens = TokenizeCommand(normalized);
        if (tokens.Count == 0)
        {
            return normalized;
        }

        for (var length = tokens.Count; length > 0; length--)
        {
            var prefix = string.Join(' ', tokens.Take(length)).ToLowerInvariant();
            if (CommandArities.TryGetValue(prefix, out var arity))
            {
                return string.Join(' ', tokens.Take(Math.Min(arity, tokens.Count)));
            }
        }

        return tokens[0];
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
               command.Contains("dotnet build", StringComparison.Ordinal) ||
               command.Contains("npm test", StringComparison.Ordinal) ||
               command.Contains("npm run build", StringComparison.Ordinal) ||
               command.Contains("pnpm test", StringComparison.Ordinal) ||
               command.Contains("pnpm build", StringComparison.Ordinal) ||
               command.Contains("yarn test", StringComparison.Ordinal) ||
               command.Contains("yarn build", StringComparison.Ordinal) ||
               command.Contains("python -m pytest", StringComparison.Ordinal) ||
               command.Contains("pytest", StringComparison.Ordinal) ||
               command.Contains("docker compose config", StringComparison.Ordinal);
    }

    private static bool IsSafeReadShellCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) ||
            Regex.IsMatch(command, @"(?:&&|\|\||[;|<>])"))
        {
            return false;
        }

        var tokens = TokenizeCommand(NormalizeShellCommand(command));
        if (tokens.Count == 0)
        {
            return false;
        }

        var executable = tokens[0].ToLowerInvariant();
        if (SafeReadCommands.Contains(executable))
        {
            return true;
        }

        return executable == "git" &&
               tokens.Count >= 2 &&
               SafeReadGitSubcommands.Contains(tokens[1].ToLowerInvariant());
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

    private static string NormalizeShellCommand(string command)
    {
        var normalized = command.Trim();
        normalized = Regex.Replace(normalized, @"^(cmd(\.exe)?\s+/c|powershell(\.exe)?\s+-command)\s+", string.Empty, RegexOptions.IgnoreCase);

        var segments = Regex.Split(normalized, @"\s*(?:&&|;|\|\|)\s+")
            .Select(segment => segment.Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();
        if (segments.Count == 0)
        {
            return normalized;
        }

        return segments.FirstOrDefault(segment =>
            !segment.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) &&
            !segment.Equals("cd", StringComparison.OrdinalIgnoreCase)) ?? segments[0];
    }

    private static IReadOnlyList<string> TokenizeCommand(string command)
    {
        var matches = Regex.Matches(command, @"[^\s""']+|""([^""]*)""|'([^']*)'");
        return matches
            .Select(match =>
                match.Groups[1].Success ? match.Groups[1].Value :
                match.Groups[2].Success ? match.Groups[2].Value :
                match.Value)
            .Where(token => !string.IsNullOrWhiteSpace(token) && !token.StartsWith("-", StringComparison.Ordinal))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, int> CommandArities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = 2,
        ["npm"] = 2,
        ["npm run"] = 3,
        ["pnpm"] = 2,
        ["pnpm run"] = 3,
        ["yarn"] = 2,
        ["yarn run"] = 3,
        ["bun"] = 2,
        ["bun run"] = 3,
        ["python"] = 2,
        ["docker"] = 2,
        ["docker compose"] = 3,
        ["git"] = 2,
        ["git config"] = 3,
        ["git remote"] = 3,
        ["git stash"] = 3,
        ["cargo"] = 2,
        ["cargo run"] = 3,
        ["go"] = 2,
        ["mvn"] = 2,
        ["gradle"] = 2,
        ["make"] = 2,
        ["cmake"] = 2,
        ["kubectl"] = 2,
        ["gh"] = 3,
        ["vercel"] = 2,
        ["npx"] = 2,
        ["pytest"] = 1,
        ["rg"] = 1
    };

    private static readonly HashSet<string> SafeReadCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "get-childitem",
        "gci",
        "ls",
        "dir",
        "get-content",
        "gc",
        "type",
        "pwd",
        "get-location",
        "rg",
        "findstr"
    };

    private static readonly HashSet<string> SafeReadGitSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "log",
        "show",
        "diff",
        "branch",
        "remote"
    };
}
