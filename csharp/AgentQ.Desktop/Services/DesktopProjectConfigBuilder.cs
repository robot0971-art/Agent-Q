namespace AgentQ.Desktop.Services;

public static class DesktopProjectConfigBuilder
{
    public static ProjectAgentConfig Build(
        AgentWorkMode workMode,
        IEnumerable<string> workspaceVerificationCommands,
        IEnumerable<string> workspaceHints)
    {
        var commands = workspaceVerificationCommands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rules = workspaceHints
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (rules.Count == 0)
        {
            rules.Add("Respect current git changes. Do not discard user edits.");
        }

        return new ProjectAgentConfig
        {
            WorkMode = workMode.ToString(),
            VerificationCommands = commands,
            WorkspaceRules = rules
        };
    }

    public static string BuildDisplay(ProjectAgentConfig config)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Project config: .agentq/config.json");
        builder.AppendLine($"Work mode: {config.WorkMode}");
        builder.AppendLine($"Updated: {config.UpdatedAt:yyyy-MM-dd HH:mm:ss}");

        if (config.VerificationCommands.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Verification commands:");
            foreach (var command in config.VerificationCommands)
            {
                builder.AppendLine($"- {command}");
            }
        }

        if (config.WorkspaceRules.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Workspace rules:");
            foreach (var rule in config.WorkspaceRules)
            {
                builder.AppendLine($"- {rule}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
