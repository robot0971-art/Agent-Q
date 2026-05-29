namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanValidationResult
{
    public List<WorkerPlanValidationIssue> Issues { get; init; } = [];

    public bool IsValid => Issues.All(issue => issue.Severity != WorkerPlanValidationSeverity.Blocker);

    public bool RequiresApproval => Issues.Any(issue => issue.Severity == WorkerPlanValidationSeverity.ApprovalRequired);
}

public sealed class WorkerPlanValidationIssue
{
    public WorkerPlanValidationSeverity Severity { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;
}

public enum WorkerPlanValidationSeverity
{
    Info,
    ApprovalRequired,
    Blocker
}
