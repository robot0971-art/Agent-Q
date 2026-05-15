namespace AgentQ.Desktop.Services;

public sealed class ToolPermissionAssessment
{
    public required PermissionRiskLevel RiskLevel { get; init; }

    public required string Operation { get; init; }

    public string Target { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool IsBlocked => RiskLevel == PermissionRiskLevel.Destructive;

    public string Summary => string.IsNullOrWhiteSpace(Target)
        ? $"{RiskLevel}: {Operation}"
        : $"{RiskLevel}: {Operation} ({Target})";
}
