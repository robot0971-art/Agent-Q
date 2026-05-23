using System.IO;
using System.Diagnostics;
using System.Text.Json;

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
            "semantic_search" => TryGetString(input, "query", out var query)
                ? $"Semantic search query: {query}. Reason: meaning-based lookup against the local embedding index."
                : "Semantic search requested against the local embedding index.",
            "hybrid_search" => TryGetString(input, "query", out var hybridQuery)
                ? $"Hybrid search query: {hybridQuery}. Reason: combined symbol, dependency graph, Git recency, project memory, semantic, keyword, and project-map ranking before reading files."
                : "Hybrid search requested to rank candidate files across symbol, graph, Git, memory, semantic, keyword, and project-map signals.",
            "symbol_search" => TryGetString(input, "query", out var symbolQuery)
                ? $"Symbol search query: {symbolQuery}. Reason: symbol index lookup to find candidate files and definitions before reading code."
                : "Symbol search requested against the local workspace symbol index.",
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
        var symbolReason = DescribeSymbolReason(path, workspaceRoot);
        var graphReason = DescribeGraphReason(normalized, workspaceRoot);
        var gitReason = DescribeGitReason(normalized, workspaceRoot);
        var memoryReason = DescribeMemoryReason(normalized, workspaceRoot);
        var extraReasons = $"{symbolReason}{graphReason}{gitReason}{memoryReason}";
        if (IsKeyProjectFile(normalized))
        {
            return $" Reason: key project file.{extraReasons}";
        }

        var role = DetectPathRole(normalized);
        return string.IsNullOrWhiteSpace(role)
            ? $" Reason: model selected this path for workspace evidence.{extraReasons}"
            : $" Reason: path maps to {role}.{extraReasons}";
    }

    private static string DescribeSymbolReason(string path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) ||
            !IsSupportedSymbolPath(path))
        {
            return string.Empty;
        }

        var symbols = new WorkspaceSymbolIndexService()
            .BuildForFile(workspaceRoot, path)
            .Where(symbol => symbol.Kind is "class" or "record" or "interface" or "struct" or "method" or "function")
            .Take(3)
            .Select(symbol => string.IsNullOrWhiteSpace(symbol.Container)
                ? $"{symbol.Kind} {symbol.Name}"
                : $"{symbol.Kind} {symbol.Container}.{symbol.Name}")
            .ToList();

        return symbols.Count == 0
            ? string.Empty
            : $" Contains symbols: {string.Join(", ", symbols)}.";
    }

    private static string DescribeGraphReason(string normalizedPath, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        var graph = new WorkspaceDependencyGraphService().Build(workspaceRoot);
        if (graph.EdgeCount == 0)
        {
            return string.Empty;
        }

        var imports = graph.Edges
            .Where(edge => edge.FromPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(edge.ToPath))
            .Select(edge => edge.ToPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        var importedBy = graph.Edges
            .Where(edge => edge.ToPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            .Select(edge => edge.FromPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var parts = new List<string>();
        if (importedBy.Count > 0)
        {
            parts.Add($"imported by {string.Join(", ", importedBy)}");
        }

        if (imports.Count > 0)
        {
            parts.Add($"imports {string.Join(", ", imports)}");
        }

        return parts.Count == 0
            ? string.Empty
            : $" Graph: {string.Join("; ", parts)}.";
    }

    private static string DescribeGitReason(string normalizedPath, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) ||
            string.IsNullOrWhiteSpace(normalizedPath) ||
            !Directory.Exists(Path.Combine(workspaceRoot, ".git")))
        {
            return string.Empty;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("status");
            startInfo.ArgumentList.Add("--porcelain=v1");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(normalizedPath);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return string.Empty;
            }

            if (!process.WaitForExit(1200) || process.ExitCode != 0)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            return string.IsNullOrWhiteSpace(output)
                ? string.Empty
                : " Git: file has local changes.";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeMemoryReason(string normalizedPath, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        var path = Path.Combine(workspaceRoot, ".agentq", "memory.local.json");
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("lessons", out var lessons) ||
                lessons.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(normalizedPath);
            foreach (var lesson in lessons.EnumerateArray())
            {
                if (lesson.TryGetProperty("enabled", out var enabled) &&
                    enabled.ValueKind == JsonValueKind.False)
                {
                    continue;
                }

                var title = lesson.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString() ?? string.Empty
                    : string.Empty;
                var content = lesson.TryGetProperty("content", out var contentElement)
                    ? contentElement.GetString() ?? string.Empty
                    : string.Empty;
                var text = $"{title}\n{content}";
                if (text.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(fileName) && text.Contains(fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    return string.IsNullOrWhiteSpace(title)
                        ? " Memory: local project memory mentions this file."
                        : $" Memory: local lesson \"{TrimEvidence(title)}\" mentions this file.";
                }
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static bool IsSupportedSymbolPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".py", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase);
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
