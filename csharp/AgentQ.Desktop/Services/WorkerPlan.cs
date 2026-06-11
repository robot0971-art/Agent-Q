namespace AgentQ.Desktop.Services;

public class WorkerPlan
{
    public string Goal { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<WorkerPlanStep> Steps { get; set; } = [];

    public List<string> VerificationCommands { get; set; } = [];

    public List<string> Risks { get; set; } = [];
}

public sealed class WorkerRepairPlan : WorkerPlan
{
    public string FailureSignature { get; set; } = string.Empty;

    public string FailureKind { get; set; } = string.Empty;

    public string SuggestedNextStep { get; set; } = string.Empty;

    public List<string> Evidence { get; set; } = [];
}

public sealed class WorkerPlanStep
{
    public WorkerPlanStepKind Kind { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string ExpectedChange { get; set; } = string.Empty;

    public bool RequiresApproval { get; set; }
}

public enum WorkerPlanStepKind
{
    CreateFile,
    ModifyFile,
    DeleteFile,
    RunCommand,
    Verify,
    Manual
}
