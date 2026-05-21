using System.IO;

namespace AgentQ.Desktop.Services;

public static class DesktopEvidenceFormatter
{
    public static string DescribeToolEvidence(
        string toolName,
        IReadOnlyDictionary<string, object?> input,
        string workspaceRoot)
    {
        return toolName switch
        {
            "read_file" => TryGetString(input, "path", out var path)
                ? $"Read file: {path}{DescribePathReason(path, workspaceRoot)}"
                : "Read file evidence requested.",
            "grep_search" => TryGetString(input, "pattern", out var pattern)
                ? $"Searched text pattern: {pattern}{FormatOptionalPath(input, workspaceRoot)}"
                : "Searched workspace text. Reason: broad search to locate candidate evidence.",
            "glob_search" => TryGetString(input, "pattern", out var glob)
                ? $"Searched file pattern: {glob}{FormatOptionalPath(input, workspaceRoot)}"
                : "Searched workspace files. Reason: broad search to discover relevant project files.",
            "bash" => TryGetString(input, "command", out var command)
                ? $"Ran command: {command}{DescribeCommandReason(command)}"
                : "Ran shell command.",
            "write_file" or "edit_file" => TryGetString(input, "path", out var target)
                ? $"Prepared file mutation: {target}{DescribePathReason(target, workspaceRoot)}"
                : "Prepared file mutation.",
            _ => string.Empty
        };
    }

    private static string FormatOptionalPath(IReadOnlyDictionary<string, object?> input, string workspaceRoot)
    {
        return TryGetString(input, "path", out var path) && !string.IsNullOrWhiteSpace(path)
            ? $" in {path}{DescribePathReason(path, workspaceRoot)}"
            : " Reason: broad workspace search to locate candidate evidence.";
    }

    private static string DescribePathReason(string path, string workspaceRoot)
    {
        var normalized = NormalizePath(path, workspaceRoot);
        if (IsKeyProjectFile(normalized))
        {
            return " Reason: key project file.";
        }

        var role = DetectPathRole(normalized);
        return string.IsNullOrWhiteSpace(role)
            ? " Reason: model selected this path for workspace evidence."
            : $" Reason: path maps to {role}.";
    }

    private static string DescribeCommandReason(string command)
    {
        var lower = command.ToLowerInvariant();
        if (lower.Contains("git ", StringComparison.Ordinal) || lower.StartsWith("git", StringComparison.Ordinal))
        {
            return " Reason: Git state evidence.";
        }

        if (lower.Contains("test", StringComparison.Ordinal) ||
            lower.Contains("build", StringComparison.Ordinal) ||
            lower.Contains("lint", StringComparison.Ordinal) ||
            lower.Contains("dotnet", StringComparison.Ordinal) ||
            lower.Contains("npm", StringComparison.Ordinal) ||
            lower.Contains("pnpm", StringComparison.Ordinal))
        {
            return " Reason: verification or build evidence.";
        }

        return " Reason: command output can confirm workspace state.";
    }

    private static string NormalizePath(string path, string workspaceRoot)
    {
        var trimmed = TrimEvidence(path).Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return trimmed;
        }

        var root = workspaceRoot.Replace('\\', '/').TrimEnd('/');
        return trimmed.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? trimmed[root.Length..].TrimStart('/')
            : trimmed;
    }

    private static bool IsKeyProjectFile(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var keyFiles = new[]
        {
            "README.md",
            "package.json",
            "pyproject.toml",
            "requirements.txt",
            "docker-compose.yml",
            "Dockerfile",
            "Program.cs",
            "App.xaml",
            "ProjectVersion.txt",
            "config.json",
            "memory.shared.json"
        };

        return keyFiles.Any(keyFile => string.Equals(fileName, keyFile, StringComparison.OrdinalIgnoreCase)) ||
            fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectPathRole(string normalizedPath)
    {
        var segments = normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (segments.Overlaps(["components", "views", "ui", "pages", "app", "src"]))
        {
            return "UI layer";
        }

        if (segments.Overlaps(["api", "controllers", "routes", "endpoints"]))
        {
            return "API layer";
        }

        if (segments.Overlaps(["db", "database", "migrations", "repositories", "data"]))
        {
            return "Database layer";
        }

        if (segments.Overlaps(["domain", "core", "services", "features", "logic"]))
        {
            return "Domain logic";
        }

        if (segments.Overlaps(["test", "tests", "__tests__", "spec", "specs"]))
        {
            return "Tests";
        }

        if (segments.Overlaps(["assets", "public", "static", "wwwroot"]))
        {
            return "Assets";
        }

        if (segments.Overlaps([".github", ".agentq", "config", "settings"]))
        {
            return "Configuration";
        }

        if (segments.Overlaps(["projectsettings", "packages"]))
        {
            return "Unity project structure";
        }

        return string.Empty;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, object?> input, string key, out string value)
    {
        if (input.TryGetValue(key, out var raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = TrimEvidence(text);
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string TrimEvidence(string value)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 220 ? value : value[..220] + "...";
    }
}
