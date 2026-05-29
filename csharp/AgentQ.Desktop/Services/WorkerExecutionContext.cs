namespace AgentQ.Desktop.Services;

public sealed class WorkerExecutionContext
{
    public required WorkerPlan Plan { get; init; }

    public required WorkerPlanPreview Preview { get; init; }

    public WorkerExecutionState State { get; set; }

    public List<AgentVerificationPlan> VerificationPlans { get; init; } = [];

    public AutoFixLoopGuardState LoopGuardState { get; set; } = AutoFixLoopGuardState.Empty;

    public WorkerRepairPlan? RepairPlan { get; set; }

    public WorkerScaffoldExecutionResult? ScaffoldResult { get; set; }

    public string StatusMessage { get; set; } = string.Empty;
}

public enum WorkerExecutionState
{
    Ready,
    AwaitingApproval,
    Blocked,
    ScaffoldExecuted,
    ScaffoldFailed,
    Succeeded,
    RepairRequired,
    StoppedRepeatedFailure
}
