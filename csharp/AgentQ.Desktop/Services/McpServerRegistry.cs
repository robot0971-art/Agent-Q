using System.IO;

namespace AgentQ.Desktop.Services;

public static class McpServerRegistry
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "node",
        "node.exe",
        "npx",
        "npx.cmd",
        "uvx",
        "uvx.exe",
        "dotnet",
        "dotnet.exe",
        "python",
        "python.exe",
        "python3",
        "python3.exe",
        "deno",
        "deno.exe",
        "bun",
        "bun.exe"
    };

    public static IReadOnlyList<McpServerConfig> EnabledServers(ProjectAgentConfig? config, string? workspaceRoot = null)
    {
        return config?.McpServers
            .Where(server => server.Enabled && IsAllowed(server, workspaceRoot))
            .ToList() ?? [];
    }

    public static string BuildContext(ProjectAgentConfig? config)
    {
        var servers = EnabledServers(config);
        if (servers.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            "Configured MCP servers:",
            "These are project-level external tool server candidates. Use native AgentQ tools unless an MCP bridge tool is available."
        };

        foreach (var server in servers)
        {
            lines.Add($"- {server.DisplayText}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static IReadOnlyList<string> Validate(ProjectAgentConfig config)
    {
        var warnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in config.McpServers)
        {
            if (!server.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(server.Name))
            {
                warnings.Add("MCP server skipped: missing name.");
                continue;
            }

            if (!seen.Add(server.Name))
            {
                warnings.Add($"MCP server duplicated: {server.Name}.");
            }

            if (!HasRequiredShape(server))
            {
                warnings.Add($"MCP server invalid: {server.Name} needs transport=stdio and a command.");
                continue;
            }

            if (!IsTrusted(server))
            {
                warnings.Add($"MCP server disabled until trusted: {server.Name} needs a trusted tag.");
            }

            if (!HasAllowedCommand(server))
            {
                warnings.Add($"MCP server command blocked: {server.Name} uses '{server.Command}'.");
            }

            if (IsWorkspaceLocalCommand(server, workspaceRoot: null))
            {
                warnings.Add($"MCP server command blocked: {server.Name} points at a workspace-local executable.");
            }
        }

        return warnings;
    }

    public static IReadOnlyList<string> Validate(ProjectAgentConfig config, string workspaceRoot)
    {
        var warnings = Validate(config).ToList();

        foreach (var server in config.McpServers.Where(HasRequiredShape))
        {
            if (IsWorkspaceLocalCommand(server, workspaceRoot))
            {
                warnings.Add($"MCP server command blocked: {server.Name} points at a workspace-local executable.");
            }

            if (!HasSafeWorkingDirectory(server, workspaceRoot))
            {
                warnings.Add($"MCP server working directory blocked: {server.Name} must stay inside the workspace.");
            }
        }

        return warnings;
    }

    private static bool IsAllowed(McpServerConfig server, string? workspaceRoot)
    {
        return HasRequiredShape(server) &&
               IsTrusted(server) &&
               HasAllowedCommand(server) &&
               !IsWorkspaceLocalCommand(server, workspaceRoot) &&
               HasSafeWorkingDirectory(server, workspaceRoot);
    }

    private static bool HasRequiredShape(McpServerConfig server)
    {
        return string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(server.Command);
    }

    private static bool IsTrusted(McpServerConfig server)
    {
        return server.Tags.Any(tag => tag.Equals("trusted", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAllowedCommand(McpServerConfig server)
    {
        return AllowedCommands.Contains(Path.GetFileName(server.Command));
    }

    private static bool IsWorkspaceLocalCommand(McpServerConfig server, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Path.IsPathRooted(server.Command))
        {
            return false;
        }

        return IsPathInside(Path.GetFullPath(server.Command), Path.GetFullPath(workspaceRoot));
    }

    private static bool HasSafeWorkingDirectory(McpServerConfig server, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(server.WorkingDirectory) || string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return true;
        }

        var fullWorkingDirectory = Path.IsPathRooted(server.WorkingDirectory)
            ? Path.GetFullPath(server.WorkingDirectory)
            : Path.GetFullPath(Path.Combine(workspaceRoot, server.WorkingDirectory));

        return IsPathInside(fullWorkingDirectory, Path.GetFullPath(workspaceRoot));
    }

    private static bool IsPathInside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ||
               (!relative.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative));
    }
}
