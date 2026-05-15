namespace AgentQ.Desktop.Services;

public enum AgentRunState
{
    Idle,
    Planning,
    GatheringContext,
    Generating,
    RunningTool,
    WaitingForApproval,
    RecordingChanges,
    Verifying,
    Done,
    Failed,
    Cancelled
}
