namespace AgentQ.Desktop.Services;

public sealed class ToolPermissionPolicyResult
{
    public required ToolPermissionAssessment Assessment { get; init; }

    public required ToolPermissionDecision Decision { get; init; }

    public string PolicyReason { get; init; } = string.Empty;

    public bool IsBlocked => Decision == ToolPermissionDecision.Block;
}
