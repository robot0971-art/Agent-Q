namespace AgentQ.Runtime.Runs;

public enum AgentRunStatus
{
    Received,
    Understanding,
    Conversation,
    AwaitingClarification,
    Planning,
    AwaitingApproval,
    ReadyToExecute,
    Executing,
    Verifying,
    Repairing,
    Recovering,
    Completed,
    Failed,
    Cancelled,
    RolledBack
}
