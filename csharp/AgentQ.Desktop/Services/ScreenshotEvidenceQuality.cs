namespace AgentQ.Desktop.Services;

public sealed class ScreenshotEvidenceQuality
{
    public required string Path { get; init; }

    public required ScreenshotEvidenceQualityStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public long SizeBytes { get; init; }
}

public enum ScreenshotEvidenceQualityStatus
{
    Valid,
    Missing,
    Empty,
    TooSmall,
    UnsupportedExtension,
    Duplicate
}
