using AgentQ.Runtime.Contracts;
using AgentQ.Runtime.Evidence;

namespace AgentQ.Runtime.Completion;

public sealed record CompletionEvaluation(
    bool IsComplete,
    IReadOnlyList<string> SatisfiedConditions,
    IReadOnlyList<string> MissingConditions,
    IReadOnlyList<string> EvidenceIds);

public interface ICompletionEvaluator
{
    CompletionEvaluation Evaluate(RuntimeTaskContract contract, IReadOnlyList<RunEvidence> evidence);
}

/// <summary>
/// Portable, conservative completion guard for hosts that still own their detailed
/// task-contract model. It deliberately only blocks a completion when a mutating or
/// command-oriented action has no corresponding execution evidence.
/// </summary>
public sealed record CompletionSafetyRequest(
    bool IsActionable,
    bool IsReadonly,
    AgentQ.Runtime.Intent.AgentTurnIntent Intent,
    bool RequiresExecutionEvidence,
    bool HasCommandEvidence,
    bool HasMutationEvidence,
    bool HasSearchEvidence);

public interface ICompletionSafetyPolicy
{
    bool RequiresRetryOrReject(CompletionSafetyRequest request);
}

public sealed class CompletionSafetyPolicy : ICompletionSafetyPolicy
{
    public bool RequiresRetryOrReject(CompletionSafetyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsActionable || request.IsReadonly)
        {
            return false;
        }

        return request.RequiresExecutionEvidence &&
            request.Intent is AgentQ.Runtime.Intent.AgentTurnIntent.Action or AgentQ.Runtime.Intent.AgentTurnIntent.Hybrid &&
            !request.HasCommandEvidence && !request.HasMutationEvidence && !request.HasSearchEvidence;
    }
}

/// <summary>
/// Evidence-backed completion policy. A model answer is intentionally not an input: callers
/// must supply mutation/command/verification evidence produced by deterministic execution.
/// </summary>
public sealed class CompletionEvaluator : ICompletionEvaluator
{
    public CompletionEvaluation Evaluate(RuntimeTaskContract contract, IReadOnlyList<RunEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);

        var contractEvidence = evidence
            .Where(item => string.Equals(item.ContractId, contract.ContractId, StringComparison.Ordinal))
            .ToArray();
        var satisfied = new List<string>();
        var missing = new List<string>();

        foreach (var condition in contract.CompletionConditions)
        {
            if (IsSatisfied(condition, contractEvidence)) satisfied.Add(condition);
            else missing.Add(condition);
        }

        return new CompletionEvaluation(
            missing.Count == 0 && contract.CompletionConditions.Count > 0,
            satisfied,
            missing,
            contractEvidence.Select(item => item.EvidenceId).ToArray());
    }

    private static bool IsSatisfied(string condition, IReadOnlyList<RunEvidence> evidence)
    {
        var normalized = condition.Trim();
        if (normalized.StartsWith("verification:", StringComparison.OrdinalIgnoreCase))
        {
            return evidence.Any(item => item.Kind == RunEvidenceKind.Verification && Contains(item, normalized["verification:".Length..]));
        }

        if (normalized.StartsWith("mutation:", StringComparison.OrdinalIgnoreCase))
        {
            return evidence.Any(item => item.Kind == RunEvidenceKind.Mutation && Contains(item, normalized["mutation:".Length..]));
        }

        if (normalized.StartsWith("command:", StringComparison.OrdinalIgnoreCase))
        {
            return evidence.Any(item => item.Kind == RunEvidenceKind.Command && Contains(item, normalized["command:".Length..]));
        }

        return evidence.Any(item => Contains(item, normalized));
    }

    private static bool Contains(RunEvidence item, string expected) =>
        item.Summary.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
