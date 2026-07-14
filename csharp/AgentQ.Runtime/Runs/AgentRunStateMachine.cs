namespace AgentQ.Runtime.Runs;

public interface IAgentRunStateMachine
{
    bool CanTransition(AgentRunStatus previousStatus, AgentRunStatus nextStatus);

    AgentRunTransition Transition(AgentRunTransitionRequest request);
}

public sealed class AgentRunStateMachine : IAgentRunStateMachine
{
    private static readonly IReadOnlyDictionary<AgentRunStatus, IReadOnlySet<AgentRunStatus>> AllowedTransitions =
        new Dictionary<AgentRunStatus, IReadOnlySet<AgentRunStatus>>
        {
            [AgentRunStatus.Received] = Set(AgentRunStatus.Understanding, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Understanding] = Set(AgentRunStatus.Conversation, AgentRunStatus.AwaitingClarification, AgentRunStatus.Planning, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Conversation] = Set(AgentRunStatus.Completed, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.AwaitingClarification] = Set(AgentRunStatus.Understanding, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Planning] = Set(AgentRunStatus.AwaitingApproval, AgentRunStatus.ReadyToExecute, AgentRunStatus.AwaitingClarification, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.AwaitingApproval] = Set(AgentRunStatus.ReadyToExecute, AgentRunStatus.Planning, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.ReadyToExecute] = Set(AgentRunStatus.Executing, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Executing] = Set(AgentRunStatus.Verifying, AgentRunStatus.Repairing, AgentRunStatus.Recovering, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Verifying] = Set(AgentRunStatus.Completed, AgentRunStatus.Repairing, AgentRunStatus.Recovering, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Repairing] = Set(AgentRunStatus.Executing, AgentRunStatus.Verifying, AgentRunStatus.Recovering, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Recovering] = Set(AgentRunStatus.ReadyToExecute, AgentRunStatus.Verifying, AgentRunStatus.RolledBack, AgentRunStatus.Cancelled, AgentRunStatus.Failed),
            [AgentRunStatus.Completed] = Set(),
            [AgentRunStatus.Failed] = Set(AgentRunStatus.Recovering, AgentRunStatus.RolledBack),
            [AgentRunStatus.Cancelled] = Set(AgentRunStatus.Recovering, AgentRunStatus.RolledBack),
            [AgentRunStatus.RolledBack] = Set()
        };

    public bool CanTransition(AgentRunStatus previousStatus, AgentRunStatus nextStatus) =>
        AllowedTransitions.TryGetValue(previousStatus, out var allowed) && allowed.Contains(nextStatus);

    public AgentRunTransition Transition(AgentRunTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireValue(request.RunId, nameof(request.RunId));
        RequireValue(request.ReasonCode, nameof(request.ReasonCode));
        RequireValue(request.PolicyVersion, nameof(request.PolicyVersion));

        if (!CanTransition(request.PreviousStatus, request.NextStatus))
        {
            throw new InvalidOperationException($"Run state transition {request.PreviousStatus} -> {request.NextStatus} is not allowed.");
        }

        if (RequiresContract(request.NextStatus))
        {
            RequireValue(request.ContractId, nameof(request.ContractId));
        }

        if (request.PreviousStatus == AgentRunStatus.Verifying && request.NextStatus == AgentRunStatus.Completed)
        {
            RequireValue(request.EvidenceId, nameof(request.EvidenceId));
        }

        return new AgentRunTransition(
            request.RunId,
            request.ContractId,
            request.PreviousStatus,
            request.NextStatus,
            request.ReasonCode,
            request.PolicyVersion,
            request.EvidenceId,
            request.OccurredAt ?? DateTimeOffset.UtcNow);
    }

    private static bool RequiresContract(AgentRunStatus status) => status is
        AgentRunStatus.AwaitingApproval or
        AgentRunStatus.ReadyToExecute or
        AgentRunStatus.Executing or
        AgentRunStatus.Verifying or
        AgentRunStatus.Repairing or
        AgentRunStatus.Recovering or
        AgentRunStatus.RolledBack;

    private static void RequireValue(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }

    private static IReadOnlySet<AgentRunStatus> Set(params AgentRunStatus[] statuses) =>
        new HashSet<AgentRunStatus>(statuses);
}
