using AgentQ.Runtime.Intent;

namespace AgentQ.Desktop.Services;

/// <summary>
/// Desktop bridge for the legacy classifier and TaskContract translator. The bridge is the
/// migration seam: Runtime owns the portable routing contract while Desktop retains the
/// existing safety-tested translation behavior until the translator moves out of WPF host.
/// </summary>
public interface IDesktopIntentRoutingAdapter
{
    LlmFirstIntentRoute Route(string userText, UserTurnUnderstanding understanding, TurnIntentClassification ruleSafety);
}

public sealed class DesktopIntentRoutingAdapter(IIntentRoutingPipeline runtimePipeline) : IDesktopIntentRoutingAdapter
{
    private readonly IIntentRoutingPipeline _runtimePipeline = runtimePipeline ?? throw new ArgumentNullException(nameof(runtimePipeline));

    public LlmFirstIntentRoute Route(string userText, UserTurnUnderstanding understanding, TurnIntentClassification ruleSafety)
    {
        ArgumentNullException.ThrowIfNull(understanding);
        ArgumentNullException.ThrowIfNull(ruleSafety);

        // The legacy router retains its detailed classification rules during migration, but
        // Runtime is a real non-bypassable safety gate: it may narrow an executable legacy
        // result, never broaden one.
        var legacyRoute = LlmFirstIntentRouter.Route(userText, understanding, ruleSafety);
        var runtimeDecision = _runtimePipeline.Route(new IntentRoutingRequest(
            ToRuntimeIntent(understanding.PrimaryIntent),
            IsCurrentUserRequest: true,
            HasConcreteTarget: understanding.IsConcreteEnough || !string.IsNullOrWhiteSpace(understanding.ActualRequestedAction.Target),
            HasActionableRequest: understanding.ActualRequestedAction.ShouldExecute,
            PolicyAllowsExecution: legacyRoute.EffectiveIntent.AllowsDeterministicExecution,
            ReasonCode: "desktop-legacy-router-bridge"));

        if (legacyRoute.EffectiveIntent.AllowsDeterministicExecution && !runtimeDecision.ShouldAllowExecution)
        {
            var type = runtimeDecision.ShouldRequestClarification
                ? TurnIntentType.Ambiguous
                : TurnIntentType.Conversation;
            return legacyRoute with
            {
                EffectiveIntent = legacyRoute.EffectiveIntent with
                {
                    Type = type,
                    ActionKind = string.Empty,
                    RequiresWrite = false,
                    RequiresShell = false,
                    RequiresNetwork = false,
                    IsConcreteEnough = false,
                    Rationale = $"Runtime safety gate narrowed legacy execution: {runtimeDecision.ReasonCode}. {legacyRoute.EffectiveIntent.Rationale}"
                },
                ExecutionContract = UserIntentTranslator.Translate(string.Empty),
                Reason = $"{legacyRoute.Reason}; runtime={runtimeDecision.ReasonCode}; runtime-veto=true"
            };
        }

        return legacyRoute with { Reason = $"{legacyRoute.Reason}; runtime={runtimeDecision.ReasonCode}; runtime-veto=false" };
    }

    private static AgentTurnIntent ToRuntimeIntent(string? primaryIntent) =>
        primaryIntent?.Trim().ToLowerInvariant() switch
        {
            "action" => AgentTurnIntent.Action,
            "hybrid" => AgentTurnIntent.Hybrid,
            "ambiguous" => AgentTurnIntent.Ambiguous,
            _ => AgentTurnIntent.Conversation
        };
}
