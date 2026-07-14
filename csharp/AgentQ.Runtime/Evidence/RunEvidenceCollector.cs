namespace AgentQ.Runtime.Evidence;

public enum RunEvidenceKind
{
    Mutation,
    Command,
    Verification,
    Snapshot,
    Approval,
    Recovery,
    FinalAnswer
}

public sealed record RunEvidence(
    string EvidenceId,
    string RunId,
    string? ContractId,
    RunEvidenceKind Kind,
    string Summary,
    string? ArtifactReference,
    DateTimeOffset RecordedAt);

public interface IRunEvidenceCollector
{
    RunEvidence Record(string runId, string? contractId, RunEvidenceKind kind, string summary, string? artifactReference = null, DateTimeOffset? recordedAt = null);

    IReadOnlyList<RunEvidence> GetForRun(string runId);

    bool HasEvidence(string runId, RunEvidenceKind kind);
}

/// <summary>Run-scoped, in-memory collector. Persistence is supplied by a future run-journal adapter.</summary>
public sealed class RunEvidenceCollector : IRunEvidenceCollector
{
    private readonly List<RunEvidence> _entries = [];

    public RunEvidence Record(string runId, string? contractId, RunEvidenceKind kind, string summary, string? artifactReference = null, DateTimeOffset? recordedAt = null)
    {
        Require(runId, nameof(runId));
        Require(summary, nameof(summary));
        var evidence = new RunEvidence(Guid.NewGuid().ToString("N"), runId, contractId, kind, summary, artifactReference, recordedAt ?? DateTimeOffset.UtcNow);
        _entries.Add(evidence);
        return evidence;
    }

    public IReadOnlyList<RunEvidence> GetForRun(string runId) =>
        _entries.Where(entry => string.Equals(entry.RunId, runId, StringComparison.Ordinal)).ToArray();

    public bool HasEvidence(string runId, RunEvidenceKind kind) =>
        _entries.Any(entry => string.Equals(entry.RunId, runId, StringComparison.Ordinal) && entry.Kind == kind);

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", parameterName);
    }
}
