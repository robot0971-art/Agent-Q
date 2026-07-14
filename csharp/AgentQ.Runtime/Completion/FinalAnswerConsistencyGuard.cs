namespace AgentQ.Runtime.Completion;

public sealed record FinalAnswerConsistencyResult(
    bool IsConsistent,
    bool ShouldBlockCompletionClaim,
    string ReasonCode,
    string UserFacingSummary);

public interface IFinalAnswerConsistencyGuard
{
    FinalAnswerConsistencyResult Evaluate(string finalAnswer, CompletionEvaluation completion);
}

public sealed class FinalAnswerConsistencyGuard : IFinalAnswerConsistencyGuard
{
    public FinalAnswerConsistencyResult Evaluate(string finalAnswer, CompletionEvaluation completion)
    {
        ArgumentNullException.ThrowIfNull(finalAnswer);
        ArgumentNullException.ThrowIfNull(completion);

        if (completion.IsComplete)
        {
            return new FinalAnswerConsistencyResult(true, false, "evidence-complete", finalAnswer);
        }

        if (!ClaimsCompletion(finalAnswer))
        {
            return new FinalAnswerConsistencyResult(true, false, "no-completion-claim", finalAnswer);
        }

        var missing = completion.MissingConditions.Count == 0
            ? "required execution evidence"
            : string.Join(", ", completion.MissingConditions);
        return new FinalAnswerConsistencyResult(
            false,
            true,
            "completion-evidence-missing",
            $"Agent Q cannot report this task as complete because evidence is missing: {missing}.");
    }

    private static bool ClaimsCompletion(string answer) =>
        answer.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
        answer.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
        answer.Contains("완료", StringComparison.Ordinal);
}
