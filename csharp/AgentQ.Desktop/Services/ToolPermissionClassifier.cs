using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentQ.Desktop.Services;

public static class ToolPermissionClassifier
{
    private static readonly Regex[] DestructiveCommandPatterns =
    [
        new(@"\brm\b(?=.*(?:-[a-z]*r[a-z]*|-recursive\b|--recursive\b))(?=.*(?:-[a-z]*f[a-z]*|-force\b|--force\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\brmdir\b(?=.*(?:-[a-z]*r[a-z]*|-recursive\b|--recursive\b))(?=.*(?:-[a-z]*f[a-z]*|-force\b|--force\b))", RegexOptions.IgnoreCase | RegexOptions.Compiled),
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

    public static ToolPermissionAssessment Assess(string toolName, string inputJson, string workspaceRoot)
    {
        var input = DesktopToolInputParser.Parse(inputJson);
        return Assess(toolName, input, workspaceRoot);
    }

    public static ToolPermissionAssessment Assess(string toolName, IReadOnlyDictionary<string, object?> input)
    {
        return Assess(toolName, input, workspaceRoot: string.Empty);
    }

    public static ToolPermissionAssessment Assess(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        return toolName switch
        {
            "list_directory" or "read_file" or "grep_search" or "glob_search" or "symbol_search" or "semantic_search" or "hybrid_search" => AssessSafeRead(toolName, input),
            "write_file" or "edit_file" => AssessFileMutation(toolName, input, workspaceRoot),
            "create_directory" => AssessDirectoryCreation(input, workspaceRoot),
            "delete_path" => AssessPathDeletion(input, workspaceRoot),
            "create_project_scaffold" => AssessProjectScaffoldCreation(input, workspaceRoot),
            "verify_project_scaffold" => AssessProjectScaffoldVerification(input),
            "run_local_server" => AssessLocalServer(input, start: true),
            "stop_local_server" => AssessLocalServer(input, start: false),
            "bash" => AssessShell(input),
            "web_search" => AssessWebSearch(input),
            _ => new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = toolName,
                Reason = "This tool can affect the local workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessSafeRead(
        string toolName,
        IReadOnlyDictionary<string, object?> input)
    {
        var target = TryGetString(input, "path");
        if (string.IsNullOrWhiteSpace(target))
        {
            target = TryGetString(input, "pattern");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            target = TryGetString(input, "query");
        }

        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.SafeRead,
            Operation = toolName,
            Target = string.IsNullOrWhiteSpace(target) ? "(workspace read)" : target,
            Reason = "This inspects local workspace information without modifying files."
        };
    }

    private static ToolPermissionAssessment AssessWebSearch(IReadOnlyDictionary<string, object?> input)
    {
        var query = TryGetString(input, "query");
        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.Network,
            Operation = "Web search",
            Target = string.IsNullOrWhiteSpace(query) ? "(missing query)" : query,
            Reason = "This searches the public web and returns read-only evidence."
        };
    }

    private static ToolPermissionAssessment AssessProjectScaffoldCreation(
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        var files = TryGetPlanStrings(input, "files");
        var commands = TryGetPlanStrings(input, "verificationCommands");
        var collisions = FindExistingPlanFiles(files, workspaceRoot);
        var overwrite = TryGetBool(input, "overwriteExistingFiles");
        var request = input.TryGetValue("request", out var rawRequest) ? rawRequest as string : null;
        var target = files.Count == 0
            ? string.IsNullOrWhiteSpace(request) ? "(missing approved plan)" : request
            : string.Join(", ", files.Take(8)) + (files.Count > 8 ? $", +{files.Count - 8} more" : string.Empty);
        var commandSummary = commands.Count == 0
            ? "no verification commands"
            : "verification: " + string.Join(", ", commands.Take(3)) + (commands.Count > 3 ? $", +{commands.Count - 3} more" : string.Empty);
        var overwriteSummary = overwrite ? "existing files may be overwritten" : "existing files are not overwritten by default";
        var collisionSummary = collisions.Count == 0
            ? "no existing target-file collisions detected"
            : "existing target-file collisions: " + string.Join(", ", collisions.Take(8)) + (collisions.Count > 8 ? $", +{collisions.Count - 8} more" : string.Empty);
        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ProjectWrite,
            Operation = "Create project scaffold",
            Target = target,
            Reason = $"This will create approved scaffold files inside the selected workspace; {commandSummary}; {overwriteSummary}; {collisionSummary}."
        };
    }

    private static List<string> FindExistingPlanFiles(IReadOnlyList<string> files, string workspaceRoot)
    {
        if (files.Count == 0 || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return [];
        }

        var collisions = new List<string>();
        try
        {
            var root = Path.GetFullPath(workspaceRoot);
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(root, file));
                if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    collisions.Add(file.Replace('\\', '/'));
                }
            }
        }
        catch
        {
            return collisions;
        }

        return collisions;
    }

    private static ToolPermissionAssessment AssessProjectScaffoldVerification(IReadOnlyDictionary<string, object?> input)
    {
        var commands = TryGetPlanStrings(input, "verificationCommands");
        var command = TryGetString(input, "command");
        var selectedCommand = string.IsNullOrWhiteSpace(command) ? commands.FirstOrDefault() : command;
        var target = string.IsNullOrWhiteSpace(selectedCommand)
            ? "(missing plan verification command)"
            : selectedCommand;
        var allowedSummary = commands.Count == 0
            ? "plan contains no verification commands"
            : "plan allows: " + string.Join(", ", commands.Take(3)) + (commands.Count > 3 ? $", +{commands.Count - 3} more" : string.Empty);

        if (string.IsNullOrWhiteSpace(selectedCommand))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = "Verify project scaffold",
                Target = target,
                Reason = $"This scaffold verification request is missing an approved verification command; {allowedSummary}."
            };
        }

        if (commands.Count == 0 ||
            !commands.Contains(selectedCommand, StringComparer.Ordinal))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = "Verify project scaffold",
                Target = target,
                Reason = $"This scaffold verification command is not listed in the approved plan; {allowedSummary}."
            };
        }

        if (!VerificationCommandPolicy.IsAllowed(selectedCommand))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.ShellCommand,
                Operation = "Verify project scaffold",
                Target = target,
                Reason = $"This scaffold verification command is not allowed by the verification command policy; {allowedSummary}."
            };
        }

        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.VerificationCommand,
            Operation = "Verify project scaffold",
            Target = target,
            Reason = $"This will run a scaffold verification command in the selected workspace; {allowedSummary}."
        };
    }

    private static ToolPermissionAssessment AssessLocalServer(
        IReadOnlyDictionary<string, object?> input,
        bool start)
    {
        var command = TryGetString(input, "command");
        var url = TryGetString(input, "url");
        var processId = TryGetString(input, "processId");
        var target = start
            ? string.IsNullOrWhiteSpace(command) ? url : command
            : string.IsNullOrWhiteSpace(url) ? processId : url;

        return new ToolPermissionAssessment
        {
            RiskLevel = PermissionRiskLevel.ShellCommand,
            Operation = start ? "Start local development server" : "Stop local development server",
            Target = string.IsNullOrWhiteSpace(target) ? "(local server)" : target,
            Reason = start
                ? "This starts a local development server process for the selected workspace."
                : "This stops the recorded local development server process for the selected workspace."
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
            TryGetJsonProperty(element, propertyName, out var jsonArray) &&
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
            TryGetDictionaryValue(dictionary, propertyName, out var rawValues))
        {
            return ExtractStringList(rawValues);
        }

        if (rawPlan is IDictionary<string, object?> mutableDictionary &&
            TryGetDictionaryValue(mutableDictionary, propertyName, out var rawMutableValues))
        {
            return ExtractStringList(rawMutableValues);
        }

        var property = rawPlan.GetType().GetProperties()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return property == null ? [] : ExtractStringList(property.GetValue(rawPlan));
    }

    private static bool TryGetDictionaryValue(
        IEnumerable<KeyValuePair<string, object?>> dictionary,
        string key,
        out object? value)
    {
        foreach (var pair in dictionary)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
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

    private static ToolPermissionAssessment AssessFileMutation(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        var path = TryGetString(input, "path");
        var isWorkspacePath = IsWorkspacePath(path, workspaceRoot);
        var risk = isWorkspacePath
            ? PermissionRiskLevel.ProjectWrite
            : PermissionRiskLevel.ExternalWrite;
        if (toolName == "write_file" &&
            isWorkspacePath &&
            IsEmptyWrite(input) &&
            !TargetExists(path, workspaceRoot))
        {
            risk = PermissionRiskLevel.LowRiskProjectWrite;
        }

        return new ToolPermissionAssessment
        {
            RiskLevel = risk,
            Operation = toolName == "write_file" ? "Write file" : "Edit file",
            Target = path ?? "(missing path)",
            Reason = risk switch
            {
                PermissionRiskLevel.LowRiskProjectWrite => "This will create a new empty file inside the selected workspace without overwriting existing content.",
                PermissionRiskLevel.ProjectWrite => "This will modify a file inside the selected workspace.",
                _ => "This may modify a file outside the selected workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessDirectoryCreation(
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        var path = TryGetString(input, "path");
        var isWorkspacePath = IsWorkspacePath(path, workspaceRoot);
        var exists = TargetExists(path, workspaceRoot);
        var risk = isWorkspacePath && !exists
            ? PermissionRiskLevel.LowRiskProjectWrite
            : isWorkspacePath
                ? PermissionRiskLevel.ProjectWrite
                : PermissionRiskLevel.ExternalWrite;

        return new ToolPermissionAssessment
        {
            RiskLevel = risk,
            Operation = "Create empty folder",
            Target = path ?? "(missing path)",
            Reason = risk switch
            {
                PermissionRiskLevel.LowRiskProjectWrite => "This will create a new empty folder inside the selected workspace without overwriting existing content.",
                PermissionRiskLevel.ProjectWrite => "This targets an existing workspace path.",
                _ => "This may create a folder outside the selected workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessPathDeletion(
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        var path = TryGetString(input, "path");
        var recursive = TryGetBool(input, "recursive");
        var isWorkspacePath = IsWorkspacePath(path, workspaceRoot);
        var targetsWorkspaceRoot = IsWorkspaceRootTarget(path, workspaceRoot);
        var risk = !isWorkspacePath
            ? PermissionRiskLevel.ExternalWrite
            : targetsWorkspaceRoot || recursive
                ? PermissionRiskLevel.Destructive
                : PermissionRiskLevel.ProjectWrite;

        return new ToolPermissionAssessment
        {
            RiskLevel = risk,
            Operation = recursive ? "Delete path recursively" : "Delete path",
            Target = path ?? "(missing path)",
            Reason = risk switch
            {
                PermissionRiskLevel.ProjectWrite => "This will delete an explicit file or empty folder inside the selected workspace.",
                PermissionRiskLevel.Destructive => "This targets the workspace root or requests recursive deletion.",
                _ => "This may delete a path outside the selected workspace."
            }
        };
    }

    private static ToolPermissionAssessment AssessShell(IReadOnlyDictionary<string, object?> input)
    {
        var command = TryGetString(input, "command");
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

        if (IsVerificationCommand(command))
        {
            return new ToolPermissionAssessment
            {
                RiskLevel = PermissionRiskLevel.VerificationCommand,
                Operation = "Verification command",
                Target = target,
                Reason = "This appears to build or test the selected project."
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

    private static bool IsWorkspacePath(string? path, string workspaceRoot = "")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var rootValue = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT")
            : workspaceRoot;
        if (string.IsNullOrWhiteSpace(rootValue))
        {
            return !Path.IsPathRooted(path) &&
                   !path.Contains("..", StringComparison.Ordinal) &&
                   path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
        }

        try
        {
            var root = Path.GetFullPath(rootValue);
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsResolvedWorkspacePath(root, fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsResolvedWorkspacePath(string workspaceRoot, string fullPath)
    {
        if (!TryResolveExistingPath(workspaceRoot, out var resolvedWorkspaceRoot))
        {
            return true;
        }

        if (File.Exists(fullPath) &&
            TryResolveExistingPath(fullPath, out var resolvedFile) &&
            !IsWithinRoot(resolvedWorkspaceRoot, resolvedFile))
        {
            return false;
        }

        var directoryToCheck = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(directoryToCheck) &&
               IsWithinRoot(workspaceRoot, directoryToCheck))
        {
            if (Directory.Exists(directoryToCheck) &&
                TryResolveExistingPath(directoryToCheck, out var resolvedDirectory) &&
                !IsWithinRoot(resolvedWorkspaceRoot, resolvedDirectory))
            {
                return false;
            }

            if (PathsEqual(workspaceRoot, directoryToCheck))
            {
                break;
            }

            directoryToCheck = Path.GetDirectoryName(directoryToCheck);
        }

        return true;
    }

    private static bool TryResolveExistingPath(string path, out string resolvedPath)
    {
        resolvedPath = Path.GetFullPath(path);

        try
        {
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var target = directory.ResolveLinkTarget(returnFinalTarget: true);
                resolvedPath = Path.GetFullPath(target?.FullName ?? directory.FullName);
                return true;
            }

            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                var target = file.ResolveLinkTarget(returnFinalTarget: true);
                resolvedPath = Path.GetFullPath(target?.FullName ?? file.FullName);
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return candidatePath.Equals(rootPath, comparison) ||
               candidatePath.StartsWith(normalizedRoot, comparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            comparison);
    }

    private static bool IsEmptyWrite(IReadOnlyDictionary<string, object?> input)
    {
        if (!input.TryGetValue("content", out var rawContent) || rawContent == null)
        {
            return false;
        }

        return rawContent switch
        {
            string text => text.Length == 0,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString()?.Length == 0,
            _ => false
        };
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null
        };
    }

    private static bool TargetExists(string? path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            var root = string.IsNullOrWhiteSpace(workspaceRoot)
                ? Environment.GetEnvironmentVariable("AGENTQ_WORKSPACE_ROOT")
                : workspaceRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return Path.IsPathRooted(path) && (File.Exists(path) || Directory.Exists(path));
            }

            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Path.GetFullPath(root), path));
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsWorkspaceRootTarget(string? path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsVerificationCommand(string command) =>
        VerificationCommandPolicy.IsAllowed(command);

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
