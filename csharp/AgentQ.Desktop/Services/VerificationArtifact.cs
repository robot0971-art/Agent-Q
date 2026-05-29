namespace AgentQ.Desktop.Services;

public sealed class VerificationArtifact
{
    public required string Kind { get; init; }

    public required string Path { get; init; }

    public string Description { get; init; } = string.Empty;
}
