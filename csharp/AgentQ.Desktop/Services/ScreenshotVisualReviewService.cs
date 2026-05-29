using System.IO;

namespace AgentQ.Desktop.Services;

public sealed class ScreenshotVisualReviewService
{
    private readonly ScreenshotEvidenceQualityChecker _qualityChecker = new();
    private readonly ScreenshotVisualHeuristicEvaluator _heuristicEvaluator = new();

    public IReadOnlyList<ScreenshotVisualReviewCandidate> SelectCandidates(
        IReadOnlyList<VerificationArtifact> artifacts,
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var qualityByPath = _qualityChecker.Check(artifacts, workspaceRoot)
            .ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

        return artifacts
            .Where(artifact => artifact.Kind.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
            .Where(artifact => qualityByPath.TryGetValue(artifact.Path, out var quality) &&
                               quality.Status == ScreenshotEvidenceQualityStatus.Valid)
            .Select(artifact => CreateCandidate(artifact, workspaceRoot))
            .Where(candidate => candidate != null)
            .Cast<ScreenshotVisualReviewCandidate>()
            .Take(4)
            .ToList();
    }

    public IReadOnlyList<string> BuildEvidence(
        IReadOnlyList<VerificationArtifact> artifacts,
        string workspaceRoot)
    {
        return Review(artifacts, workspaceRoot)
            .Select(FormatReviewEvidence)
            .ToList();
    }

    public IReadOnlyList<ScreenshotVisualReviewResult> Review(
        IReadOnlyList<VerificationArtifact> artifacts,
        string workspaceRoot)
    {
        return SelectCandidates(artifacts, workspaceRoot)
            .Select(_heuristicEvaluator.Evaluate)
            .ToList();
    }

    private static ScreenshotVisualReviewCandidate? CreateCandidate(
        VerificationArtifact artifact,
        string workspaceRoot)
    {
        var fullPath = ResolvePath(workspaceRoot, artifact.Path);
        if (fullPath == null)
        {
            return null;
        }

        return new ScreenshotVisualReviewCandidate
        {
            RelativePath = artifact.Path.Replace('\\', '/'),
            FullPath = fullPath,
            Reason = "Valid Playwright screenshot should be reviewed for blank screens, overlap, clipping, and broken layout."
        };
    }

    private static string? ResolvePath(string workspaceRoot, string artifactPath)
    {
        if (Path.IsPathRooted(artifactPath))
        {
            return null;
        }

        var root = Path.GetFullPath(workspaceRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, artifactPath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string FormatReviewEvidence(ScreenshotVisualReviewResult result)
    {
        return $"Screenshot visual review {result.Status}: {result.RelativePath}. {result.Message} brightness={result.AverageBrightness:0.000}, variance={result.BrightnessVariance:0.0000}";
    }
}
