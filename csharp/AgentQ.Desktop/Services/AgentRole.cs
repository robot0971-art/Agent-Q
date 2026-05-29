using System;
using System.Collections.Generic;

namespace AgentQ.Desktop.Services;

public sealed class AgentRole
{
    public required MultiAgentRole Role { get; init; }
    public required string SystemPromptOverride { get; init; }
    public required IReadOnlyList<string> AllowedTools { get; init; }
}

public static class AgentRoleCatalog
{
    public static AgentRole ForRole(MultiAgentRole role) => role switch
    {
        MultiAgentRole.Planner => Planner,
        MultiAgentRole.Coder => Coder,
        MultiAgentRole.Reviewer => Reviewer,
        MultiAgentRole.Tester => Tester,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    public static AgentRole Planner => new()
    {
        Role = MultiAgentRole.Planner,
        SystemPromptOverride = "You are a planning agent. Analyze the codebase and break the task into steps. Do NOT edit files.",
        AllowedTools = new[] { "read_file", "grep_search", "glob_search", "symbol_search", "hybrid_search" }
    };

    public static AgentRole Coder => new()
    {
        Role = MultiAgentRole.Coder,
        SystemPromptOverride = "You are a coding agent. Implement the specified task using minimal, focused changes.",
        AllowedTools = new[] { "read_file", "write_file", "edit_file", "bash", "run_command", "grep_search", "symbol_search" }
    };

    public static AgentRole Reviewer => new()
    {
        Role = MultiAgentRole.Reviewer,
        SystemPromptOverride = "You are a code review agent. Review the changes for bugs, regressions, and missing edge cases. Do NOT edit files.",
        AllowedTools = new[] { "read_file", "grep_search", "symbol_search" }
    };

    public static AgentRole Tester => new()
    {
        Role = MultiAgentRole.Tester,
        SystemPromptOverride = "You are a testing agent. Run verification commands and report results.",
        AllowedTools = new[] { "read_file", "bash", "run_command", "grep_search" }
    };
}
