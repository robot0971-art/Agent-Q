namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanPreview
{
    public WorkerPlan Plan { get; init; } = new();

    public WorkerPlanApprovalSummary ApprovalSummary { get; init; } = new();

    public WorkerPlanValidationResult Validation { get; init; } = new();

    public WorkerPlanApprovalState ApprovalState { get; init; }

    public string DecisionSummary { get; init; } = string.Empty;
}

public enum WorkerPlanApprovalState
{
    Ready,
    NeedsApproval,
    Blocked
}
