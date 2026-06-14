namespace AgentQ.Desktop.Services;

public sealed record LlmFirstIntentRoute
{
    public TurnIntentClassification EffectiveIntent { get; init; } = new();

    public TaskContract ExecutionContract { get; init; } = new();

    public string RoutingText { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;
}

public static class LlmFirstIntentRouter
{
    public static LlmFirstIntentRoute Route(
        string userText,
        UserTurnUnderstanding understanding,
        TurnIntentClassification ruleSafety)
    {
        var routingText = string.IsNullOrWhiteSpace(understanding.RoutingText)
            ? userText
            : understanding.RoutingText;
        var effectiveIntent = BuildEffectiveIntent(understanding, ruleSafety);
        var executionContract = effectiveIntent.AllowsDeterministicExecution
            ? UserIntentTranslator.Translate(routingText)
            : UserIntentTranslator.Translate(string.Empty);

        return new LlmFirstIntentRoute
        {
            EffectiveIntent = effectiveIntent,
            ExecutionContract = executionContract,
            RoutingText = routingText,
            Reason = BuildReason(understanding, ruleSafety, effectiveIntent, executionContract)
        };
    }

    private static TurnIntentClassification BuildEffectiveIntent(
        UserTurnUnderstanding understanding,
        TurnIntentClassification ruleSafety)
    {
        var modelIntent = UserTurnUnderstandingService.ToTurnIntentClassification(understanding);
        if (!understanding.ActualRequestedAction.ShouldExecute)
        {
            var type = modelIntent.Type == TurnIntentType.Ambiguous
                ? TurnIntentType.Ambiguous
                : TurnIntentType.Conversation;
            return modelIntent with
            {
                Type = type,
                ActionKind = type == TurnIntentType.Conversation ? string.Empty : modelIntent.ActionKind,
                RequiresWrite = false,
                RequiresShell = false,
                RequiresNetwork = type == TurnIntentType.Conversation ? false : modelIntent.RequiresNetwork,
                IsConcreteEnough = type == TurnIntentType.Conversation || modelIntent.IsConcreteEnough,
                Rationale =
                    $"LLM-first router accepted the non-executing UserTurnUnderstanding ({understanding.PrimaryIntent}). Rule safety was {ruleSafety.Type}: {ruleSafety.Rationale}"
            };
        }

        var actionType = modelIntent.Type is TurnIntentType.Action or TurnIntentType.Hybrid
            ? modelIntent.Type
            : TurnIntentType.Action;
        if (ruleSafety.Type == TurnIntentType.Ambiguous && !ruleSafety.IsConcreteEnough)
        {
            return ruleSafety with
            {
                Rationale =
                    $"LLM-first router found an execution-looking request, but guard review kept the turn Ambiguous because the rule safety pass did not find a concrete target. UserTurnUnderstanding was {understanding.PrimaryIntent}: {understanding.ActualRequestedAction.Reason}"
            };
        }

        var concrete = understanding.IsConcreteEnough || !string.IsNullOrWhiteSpace(understanding.ActualRequestedAction.Target);
        if (!concrete)
        {
            return modelIntent with
            {
                Type = TurnIntentType.Ambiguous,
                IsConcreteEnough = false,
                ClarifyingQuestion = string.IsNullOrWhiteSpace(modelIntent.ClarifyingQuestion)
                    ? "Please clarify the target and desired result before AgentQ executes anything."
                    : modelIntent.ClarifyingQuestion,
                RequiresWrite = false,
                RequiresShell = false,
                Rationale =
                    $"LLM-first router found an execution request, but guard review did not find a concrete target. Rule safety was {ruleSafety.Type}: {ruleSafety.Rationale}"
            };
        }

        return modelIntent with
        {
            Type = actionType,
            IsConcreteEnough = true,
            Rationale =
                $"LLM-first router accepted an execution request after guard review. Rule safety was {ruleSafety.Type}: {ruleSafety.Rationale}"
        };
    }

    private static string BuildReason(
        UserTurnUnderstanding understanding,
        TurnIntentClassification ruleSafety,
        TurnIntentClassification effectiveIntent,
        TaskContract executionContract)
    {
        var contractText = executionContract.IsActionable
            ? executionContract.Intent.ToString()
            : "none";
        return
            $"primary={understanding.PrimaryIntent}; shouldExecute={understanding.ActualRequestedAction.ShouldExecute}; " +
            $"rule={ruleSafety.Type}; effective={effectiveIntent.Type}; contract={contractText}";
    }
}
