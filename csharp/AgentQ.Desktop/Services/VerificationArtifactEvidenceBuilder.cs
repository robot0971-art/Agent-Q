namespace AgentQ.Desktop.Services;

public sealed class VerificationArtifactEvidenceBuilder
{
    private readonly ScreenshotEvidenceQualityChecker _screenshotQualityChecker = new();
    private readonly ScreenshotVisualReviewService _screenshotVisualReviewService = new();

    public IReadOnlyList<string> BuildEvidence(IReadOnlyList<VerificationArtifact> artifacts)
    {
        return artifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Path))
            .Select(FormatArtifact)
            .Take(8)
            .ToList();
    }

    public IReadOnlyList<string> BuildEvidence(
        IReadOnlyList<VerificationArtifact> artifacts,
        string workspaceRoot)
    {
        var evidence = BuildEvidence(artifacts).ToList();
        evidence.AddRange(_screenshotQualityChecker
            .Check(artifacts, workspaceRoot)
            .Select(FormatScreenshotQuality));
        evidence.AddRange(_screenshotVisualReviewService.BuildEvidence(artifacts, workspaceRoot));
        return evidence.Take(12).ToList();
    }

    public string BuildSummary(IReadOnlyList<VerificationArtifact> artifacts)
    {
        var evidence = BuildEvidence(artifacts);
        return evidence.Count == 0
            ? string.Empty
            : string.Join("; ", evidence.Take(3));
    }

    public string BuildSummary(IReadOnlyList<VerificationArtifact> artifacts, string workspaceRoot)
    {
        var evidence = BuildEvidence(artifacts, workspaceRoot);
        return evidence.Count == 0
            ? string.Empty
            : string.Join("; ", evidence.Take(3));
    }

    private static string FormatArtifact(VerificationArtifact artifact)
    {
        var description = string.IsNullOrWhiteSpace(artifact.Description)
            ? artifact.Kind
            : artifact.Description;
        return $"Artifact {artifact.Kind}: {artifact.Path} ({description})";
    }

    private static string FormatScreenshotQuality(ScreenshotEvidenceQuality quality)
    {
        var size = quality.SizeBytes > 0 ? $", {quality.SizeBytes:0} bytes" : string.Empty;
        return $"Screenshot quality {quality.Status}: {quality.Path}{size}. {quality.Message}";
    }
}
