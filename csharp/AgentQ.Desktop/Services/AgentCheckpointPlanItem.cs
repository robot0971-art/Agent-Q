namespace AgentQ.Desktop.Services;

public sealed class AgentCheckpointPlanItem
{
    public int Order { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public AgentPlanItemStatus Status { get; set; } = AgentPlanItemStatus.Pending;
}
