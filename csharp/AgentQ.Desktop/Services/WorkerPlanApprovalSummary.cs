namespace AgentQ.Desktop.Services;

public sealed class WorkerPlanApprovalSummary
{
    public int CreateCount { get; init; }

    public int ModifyCount { get; init; }

    public int DeleteCount { get; init; }

    public int RunCommandCount { get; init; }

    public List<string> CreatedFiles { get; init; } = [];

    public List<string> ModifiedFiles { get; init; } = [];

    public List<string> DeletedFiles { get; init; } = [];

    public List<string> ExpectedChanges { get; init; } = [];

    public WorkerPlanRiskLevel RiskLevel { get; init; }

    public List<string> RiskReasons { get; init; } = [];

    public List<string> VerificationCommands { get; init; } = [];

    public bool HasHighRiskChanges => RiskLevel == WorkerPlanRiskLevel.High;

    public bool CanApproveLowRiskOnly => HasHighRiskChanges &&
                                         (CreateCount + ModifyCount + DeleteCount) > DeleteCount;
}

public enum WorkerPlanRiskLevel
{
    Low,
    Medium,
    High
}
