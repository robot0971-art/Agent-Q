namespace AgentQ.Desktop.Services;

public sealed record AgentTurnState
{
    public required string TraceId { get; init; }

    public required string RawUserText { get; init; }

    public required string RoutingText { get; init; }

    public required string WorkspaceRoot { get; init; }

    public required AgentWorkMode WorkMode { get; init; }

    public required UserTurnUnderstanding Understanding { get; init; }

    public required TurnIntentClassification RuleIntent { get; init; }

    public required TurnIntentClassification EffectiveIntent { get; init; }

    public required DesktopTaskProfile TaskProfile { get; init; }

    public required TaskContract TaskContract { get; init; }

    public required ProjectScaffoldPlanningResult ProjectScaffoldPlan { get; init; }

    public required IReadOnlyList<AgentQSystemSkill> SelectedSystemSkills { get; init; }

    public ProjectAgentConfig? ProjectConfig { get; init; }

    public required AgentTurnContextPolicy ContextPolicy { get; init; }

    public required AgentTurnToolPolicy ToolPolicy { get; init; }

    public required AgentTurnMemoryPolicy MemoryPolicy { get; init; }

    public required AgentTurnVerificationPolicy VerificationPolicy { get; init; }

    public required AgentTurnFinalAnswerPolicy FinalAnswerPolicy { get; init; }

    public bool HasActionableContract => TaskContract.IsActionable;

    public bool AllowsDeterministicExecution => EffectiveIntent.AllowsDeterministicExecution;

    public bool IsConversation => EffectiveIntent.Type == TurnIntentType.Conversation;

    public bool IsAmbiguous => EffectiveIntent.Type == TurnIntentType.Ambiguous;

    public bool IsActionOrHybrid => EffectiveIntent.Type is TurnIntentType.Action or TurnIntentType.Hybrid;

    public bool IsLocalServerContract =>
        TaskContract.Intent is TaskContractIntent.RunLocalServer or TaskContractIntent.StopLocalServer;

    public string Summary =>
        $"trace={TraceId}; intent={EffectiveIntent.Type}; rule={RuleIntent.Type}; contract={TaskContract.Intent}; " +
        $"profile={TaskProfile.Label}; concrete={EffectiveIntent.IsConcreteEnough}; route=\"{DesktopPromptBuilder.Truncate(RoutingText.ReplaceLineEndings(" "), 300)}\"";
}

public sealed record AgentTurnContextPolicy
{
    public required bool AttachWorkspaceContext { get; init; }

    public required bool FetchLinks { get; init; }

    public required bool IncludeScaffoldContext { get; init; }

    public required bool IncludeExecutionLessons { get; init; }

    public required bool TreatSupplementalContextAsEvidenceOnly { get; init; }
}

public sealed record AgentTurnToolPolicy
{
    public required bool AllowToolLoop { get; init; }

    public required bool BlockWriteShellAndScaffoldForConversation { get; init; }

    public required bool RequirePermissionForRiskyTools { get; init; }

    public required bool RequireEvidenceForActionCompletion { get; init; }
}

public sealed record AgentTurnMemoryPolicy
{
    public required bool SelectReadOnlyContext { get; init; }

    public required bool RecordOnlyAfterExecutionEvidence { get; init; }

    public required bool TreatMemoryAsSupplementalEvidence { get; init; }
}

public sealed record AgentTurnVerificationPolicy
{
    public required bool AllowVerification { get; init; }

    public required bool RequireAllowedCommand { get; init; }

    public required bool RequireEvidenceBeforeSuccess { get; init; }
}

public sealed record AgentTurnFinalAnswerPolicy
{
    public required bool RequireEvidenceForCompletionClaims { get; init; }

    public required bool RejectUnsupportedSuccess { get; init; }

    public required bool AskClarifyingQuestionForAmbiguous { get; init; }
}
