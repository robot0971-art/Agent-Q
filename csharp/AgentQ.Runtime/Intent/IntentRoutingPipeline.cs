namespace AgentQ.Runtime.Intent;

public enum AgentTurnIntent
{
    Conversation,
    Action,
    Hybrid,
    Ambiguous
}

public sealed record IntentRoutingRequest(
    AgentTurnIntent ModelIntent,
    bool IsCurrentUserRequest,
    bool HasConcreteTarget,
    bool HasActionableRequest,
    bool PolicyAllowsExecution,
    string ReasonCode);

public sealed record IntentRoutingDecision(
    AgentTurnIntent EffectiveIntent,
    bool ShouldBuildTaskContract,
    bool ShouldRequestClarification,
    bool ShouldAllowExecution,
    string ReasonCode);

public interface IIntentRoutingPipeline
{
    IntentRoutingDecision Route(IntentRoutingRequest request);
}

/// <summary>
/// Merges semantic classification with non-bypassable policy facts. This policy is pure:
/// callers still own permission prompts, task-contract construction, and execution.
/// </summary>
public sealed class IntentRoutingPipeline : IIntentRoutingPipeline
{
    public IntentRoutingDecision Route(IntentRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.IsCurrentUserRequest)
        {
            return Decision(AgentTurnIntent.Conversation, false, false, false, "non-current-context");
        }

        if (request.ModelIntent == AgentTurnIntent.Ambiguous ||
            (request.HasActionableRequest && !request.HasConcreteTarget))
        {
            return Decision(AgentTurnIntent.Ambiguous, false, true, false, "missing-concrete-target");
        }

        if (request.HasActionableRequest && request.HasConcreteTarget)
        {
            var effectiveIntent = request.ModelIntent == AgentTurnIntent.Hybrid
                ? AgentTurnIntent.Hybrid
                : AgentTurnIntent.Action;

            return Decision(
                effectiveIntent,
                true,
                false,
                request.PolicyAllowsExecution,
                request.PolicyAllowsExecution ? "concrete-action" : "execution-policy-blocked");
        }

        return request.ModelIntent switch
        {
            AgentTurnIntent.Hybrid => Decision(AgentTurnIntent.Conversation, false, false, false, "no-actionable-request"),
            AgentTurnIntent.Action => Decision(AgentTurnIntent.Conversation, false, false, false, "no-actionable-request"),
            _ => Decision(AgentTurnIntent.Conversation, false, false, false, "conversation")
        };
    }

    private static IntentRoutingDecision Decision(
        AgentTurnIntent intent,
        bool buildTaskContract,
        bool requestClarification,
        bool allowExecution,
        string reasonCode) =>
        new(intent, buildTaskContract, requestClarification, allowExecution, reasonCode);
}
