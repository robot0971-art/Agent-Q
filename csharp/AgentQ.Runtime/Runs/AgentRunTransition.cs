namespace AgentQ.Runtime.Runs;

public sealed record AgentRunTransition(
    string RunId,
    string? ContractId,
    AgentRunStatus PreviousStatus,
    AgentRunStatus NextStatus,
    string ReasonCode,
    string PolicyVersion,
    string? EvidenceId,
    DateTimeOffset OccurredAt);

public sealed record AgentRunTransitionRequest(
    string RunId,
    string? ContractId,
    AgentRunStatus PreviousStatus,
    AgentRunStatus NextStatus,
    string ReasonCode,
    string PolicyVersion,
    string? EvidenceId = null,
    DateTimeOffset? OccurredAt = null);
