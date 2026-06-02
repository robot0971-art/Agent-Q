namespace AgentQ.Desktop.Services;

public enum AgentRunState
{
    Idle,
    Planning,
    GatheringContext,
    Generating,
    Clarifying,
    RunningTool,
    WaitingForApproval,
    RecordingChanges,
    Verifying,
    Done,
    Failed,
    Cancelled
}
