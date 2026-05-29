namespace AgentQ.Desktop.Services;

public sealed class ScreenshotLlmVisionReviewRequest
{
    public required ScreenshotVisualReviewCandidate Candidate { get; init; }

    public ScreenshotVisualReviewResult? HeuristicResult { get; init; }

    public string VerificationOutput { get; init; } = string.Empty;

    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed class ScreenshotLlmVisionReviewResult
{
    public required string RelativePath { get; init; }

    public required ScreenshotLlmVisionReviewStatus Status { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Findings { get; init; } = [];

    public string RawResponse { get; init; } = string.Empty;
}

public enum ScreenshotLlmVisionReviewStatus
{
    Pass,
    Warning,
    Fail,
    Unknown
}
