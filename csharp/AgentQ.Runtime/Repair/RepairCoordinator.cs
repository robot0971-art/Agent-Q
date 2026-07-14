namespace AgentQ.Runtime.Repair;

public sealed record RepairPolicy(int MaximumAttempts, int MaximumRepeatsPerFingerprint);

public sealed record RepairAttempt(int AttemptNumber, string FailureFingerprint, bool RequiresScopeExpansion);

public sealed record RepairDecision(bool ShouldRepair, string ReasonCode, string? Blocker);

public interface IRepairCoordinator
{
    RepairDecision Decide(RepairPolicy policy, IReadOnlyList<RepairAttempt> history, string nextFailureFingerprint, bool requiresScopeExpansion);
}

public sealed class RepairCoordinator : IRepairCoordinator
{
    public RepairDecision Decide(RepairPolicy policy, IReadOnlyList<RepairAttempt> history, string nextFailureFingerprint, bool requiresScopeExpansion)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(history);
        if (policy.MaximumAttempts < 1 || policy.MaximumRepeatsPerFingerprint < 1)
            throw new ArgumentOutOfRangeException(nameof(policy), "Repair limits must be positive.");
        if (string.IsNullOrWhiteSpace(nextFailureFingerprint))
            throw new ArgumentException("A failure fingerprint is required.", nameof(nextFailureFingerprint));

        if (requiresScopeExpansion)
            return new RepairDecision(false, "scope-expansion-required", "Repair requires capability, target, package, or network scope beyond the approved contract.");

        if (history.Count >= policy.MaximumAttempts)
            return new RepairDecision(false, "repair-attempt-limit", "The bounded repair attempt limit has been reached.");

        var repeats = history.Count(attempt => string.Equals(attempt.FailureFingerprint, nextFailureFingerprint, StringComparison.Ordinal));
        if (repeats >= policy.MaximumRepeatsPerFingerprint)
            return new RepairDecision(false, "repeated-failure-fingerprint", "The same failure fingerprint repeated; a different diagnosis or user decision is required.");

        return new RepairDecision(true, "repair-authorized", null);
    }
}
