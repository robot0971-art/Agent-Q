namespace AgentQ.Desktop.Services;

public sealed class VerificationRunResult
{
    public required int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public IReadOnlyList<VerificationArtifact> Artifacts { get; init; } = [];

    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput => string.Join(Environment.NewLine, StandardOutput, StandardError).Trim();
}
