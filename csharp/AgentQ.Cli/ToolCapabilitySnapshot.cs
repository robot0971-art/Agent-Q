using AgentQ.Core.Providers;
using AgentQ.Tools;

namespace AgentQ.Cli;

internal sealed class ToolCapabilitySnapshot
{
    public List<ToolCapabilityEntry> Tools { get; } = [];
    public List<string> DuplicateRegistrations { get; } = [];

    public static ToolCapabilitySnapshot Create(ProviderConfiguration config, ToolRegistry registry)
    {
        var snapshot = new ToolCapabilitySnapshot();
        snapshot.DuplicateRegistrations.AddRange(registry.DuplicateRegistrations.Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var tool in registry.All.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            snapshot.Tools.Add(new ToolCapabilityEntry(
                tool.Name,
                ResolvePermission(config, tool.Name),
                tool.RequiresPermission));
        }

        return snapshot;
    }

    public string ToPromptBlock()
    {
        var allowed = Format("allowed");
        var denied = Format("denied");
        var notAllowed = Format("not-allowed");

        return
            $"""
            Tool Permission State:
            - mode: non-interactive
            - allowed tools: {allowed}
            - denied tools: {denied}
            - not allowed in this run: {notAllowed}
            - skipped duplicate tool registrations: {FormatDuplicates()}
            - permission-gated tools are already resolved by this state; if a tool is allowed, call it directly.
            - never say you lack permission for an allowed tool unless an actual tool call returns permission denied.
            """;
    }

    private string FormatDuplicates()
    {
        return DuplicateRegistrations.Count == 0
            ? "none"
            : string.Join(", ", DuplicateRegistrations.Take(12));
    }

    private string Format(string permission)
    {
        var names = Tools
            .Where(tool => tool.Permission.Equals(permission, StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.RequiresPermission ? $"{tool.Name}(permission-gated)" : tool.Name)
            .Take(24)
            .ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string ResolvePermission(ProviderConfiguration config, string toolName)
    {
        if (config.DeniedToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return "denied";
        }

        if (config.AllowToolsWithoutPrompt || config.AllowedToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return "allowed";
        }

        return "not-allowed";
    }
}

internal sealed record ToolCapabilityEntry(string Name, string Permission, bool RequiresPermission);
