namespace AgentQ.Runtime.Runs;

public interface IAgentRunCoordinator
{
    AgentRunSession Start(string runId, string policyVersion, DateTimeOffset? startedAt = null);
}

public sealed class AgentRunCoordinator(IAgentRunStateMachine stateMachine) : IAgentRunCoordinator
{
    private readonly IAgentRunStateMachine _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

    public AgentRunSession Start(string runId, string policyVersion, DateTimeOffset? startedAt = null)
    {
        var transition = new AgentRunTransition(
            RequireValue(runId, nameof(runId)),
            ContractId: null,
            PreviousStatus: AgentRunStatus.Received,
            NextStatus: AgentRunStatus.Received,
            ReasonCode: "run-received",
            PolicyVersion: RequireValue(policyVersion, nameof(policyVersion)),
            EvidenceId: null,
            OccurredAt: startedAt ?? DateTimeOffset.UtcNow);

        return new AgentRunSession(_stateMachine, transition);
    }

    private static string RequireValue(string? value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A non-empty value is required.", parameterName);
}

public sealed class AgentRunSession
{
    private readonly IAgentRunStateMachine _stateMachine;
    private readonly List<AgentRunTransition> _history;

    internal AgentRunSession(IAgentRunStateMachine stateMachine, AgentRunTransition received)
    {
        _stateMachine = stateMachine;
        _history = [received];
    }

    public string RunId => Current.RunId;

    public AgentRunStatus Status => Current.NextStatus;

    public AgentRunTransition Current => _history[^1];

    public IReadOnlyList<AgentRunTransition> History => _history;

    public AgentRunTransition Advance(
        AgentRunStatus nextStatus,
        string reasonCode,
        string? contractId = null,
        string? evidenceId = null,
        DateTimeOffset? occurredAt = null)
    {
        var transition = _stateMachine.Transition(new AgentRunTransitionRequest(
            RunId,
            contractId ?? Current.ContractId,
            Status,
            nextStatus,
            reasonCode,
            Current.PolicyVersion,
            evidenceId,
            occurredAt));

        _history.Add(transition);
        return transition;
    }
}
