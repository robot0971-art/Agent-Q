namespace AgentQ.Desktop.Services;

public sealed class ScreenshotVisualReviewResult
{
    public required string RelativePath { get; init; }

    public required ScreenshotVisualReviewStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public double AverageBrightness { get; init; }

    public double BrightnessVariance { get; init; }

    public int SampledPixels { get; init; }
}

public enum ScreenshotVisualReviewStatus
{
    Pass,
    Warning,
    Fail
}
