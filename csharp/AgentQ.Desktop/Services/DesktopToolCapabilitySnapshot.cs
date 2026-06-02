using System.Text;
using AgentQ.Tools;

namespace AgentQ.Desktop.Services;

public sealed class DesktopToolCapabilitySnapshot
{
    private readonly List<DesktopToolCapabilityEntry> _entries = [];
    private readonly List<string> _duplicateRegistrations = [];

    public static DesktopToolCapabilitySnapshot Create(ToolRegistry registry, AgentWorkMode workMode)
    {
        var snapshot = new DesktopToolCapabilitySnapshot();
        snapshot._duplicateRegistrations.AddRange(registry.DuplicateRegistrations.Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var tool in registry.All.OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase))
        {
            snapshot._entries.Add(new DesktopToolCapabilityEntry(
                tool.Name,
                ResolveDefaultState(tool, workMode),
                tool.RequiresPermission,
                Describe(tool, workMode)));
        }

        return snapshot;
    }

    public string ToPromptBlock()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Tool Permission State:");
        builder.AppendLine("- mode: desktop interactive");
        builder.AppendLine("- user approval UI is available for tools marked requires-approval.");
        builder.AppendLine("- blocked tools or blocked inputs must not be retried with equivalent risky commands.");
        builder.AppendLine("- do not claim a tool is unavailable unless policy blocked it, the user denied it, or the tool call failed.");
        AppendGroup(builder, "allowed", "allowed");
        AppendGroup(builder, "requires approval", "requires-approval");
        AppendGroup(builder, "blocked by current mode", "blocked");
        if (_duplicateRegistrations.Count > 0)
        {
            builder.AppendLine($"- skipped duplicate tool registrations: {string.Join(", ", _duplicateRegistrations.Take(12))}");
        }
        builder.AppendLine("- bash runs PowerShell on Windows from the selected workspace root; avoid Bash-only chaining such as && or ||.");
        return builder.ToString().TrimEnd();
    }

    private void AppendGroup(StringBuilder builder, string label, string state)
    {
        var items = _entries
            .Where(entry => entry.State.Equals(state, StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.Name} ({entry.Note})")
            .Take(24)
            .ToList();

        builder.AppendLine($"- {label}: {(items.Count == 0 ? "none" : string.Join(", ", items))}");
    }

    private static string ResolveDefaultState(ITool tool, AgentWorkMode workMode)
    {
        if (!tool.RequiresPermission)
        {
            return "allowed";
        }

        if (workMode == AgentWorkMode.Readonly)
        {
            return "blocked";
        }

        return "requires-approval";
    }

    private static string Describe(ITool tool, AgentWorkMode workMode)
    {
        if (!tool.RequiresPermission)
        {
            return "safe read/search";
        }

        if (workMode == AgentWorkMode.Readonly)
        {
            return "readonly mode blocks mutation and shell execution";
        }

        return tool.Name switch
        {
            "edit_file" or "write_file" => "workspace edits require approval; external writes are blocked",
            "create_project_scaffold" => "project scaffold file creation requires approval; existing files are not overwritten by default",
            "verify_project_scaffold" => "project scaffold verification requires approval; only plan-listed commands may run",
            "bash" => "verification commands require approval; destructive commands are blocked",
            _ when tool.Name.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase) => "external MCP action requires approval",
            _ => "requires user approval"
        };
    }
}

internal sealed record DesktopToolCapabilityEntry(string Name, string State, bool RequiresPermission, string Note);
