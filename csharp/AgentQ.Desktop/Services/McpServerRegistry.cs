namespace AgentQ.Desktop.Services;

public static class McpServerRegistry
{
    public static IReadOnlyList<McpServerConfig> EnabledServers(ProjectAgentConfig? config)
    {
        return config?.McpServers
            .Where(server => server.Enabled && IsValid(server))
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
            if (string.IsNullOrWhiteSpace(server.Name))
            {
                warnings.Add("MCP server skipped: missing name.");
                continue;
            }

            if (!seen.Add(server.Name))
            {
                warnings.Add($"MCP server duplicated: {server.Name}.");
            }

            if (!IsValid(server))
            {
                warnings.Add($"MCP server invalid: {server.Name} needs transport=stdio and a command.");
            }
        }

        return warnings;
    }

    private static bool IsValid(McpServerConfig server)
    {
        return string.Equals(server.Transport, "stdio", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(server.Command);
    }
}
