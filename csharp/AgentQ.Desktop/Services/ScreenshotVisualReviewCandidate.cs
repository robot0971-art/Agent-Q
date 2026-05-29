namespace AgentQ.Desktop.Services;

public sealed class ScreenshotVisualReviewCandidate
{
    public required string RelativePath { get; init; }

    public required string FullPath { get; init; }

    public string Reason { get; init; } = string.Empty;
}
