using System.IO;
using System.Net.Http;
using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopScreenshotLlmVisionWorkflowService(
    IDesktopLlmProviderFactory providerFactory,
    ScreenshotVisualReviewService visualReviewService,
    ScreenshotVisualHeuristicEvaluator heuristicEvaluator,
    ScreenshotLlmVisionEvidenceBuilder evidenceBuilder)
{
    private const int MaximumScreenshotsPerVerification = 2;

    public async Task<IReadOnlyList<string>> BuildEvidenceAsync(
        VerificationRunResult result,
        string workspaceRoot,
        ProviderConfiguration? config,
        CancellationToken ct = default)
    {
        if (config?.DesktopEnableScreenshotLlmVisionReview != true ||
            result.Artifacts.Count == 0)
        {
            return [];
        }

        var candidates = visualReviewService
            .SelectCandidates(result.Artifacts, workspaceRoot)
            .Take(MaximumScreenshotsPerVerification)
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var provider = providerFactory.CreateProvider(config);
        var reviewer = new ScreenshotLlmVisionReviewer(provider);
        var reviews = new List<ScreenshotLlmVisionReviewResult>();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                reviews.Add(await reviewer.ReviewAsync(
                    new ScreenshotLlmVisionReviewRequest
                    {
                        Candidate = candidate,
                        HeuristicResult = heuristicEvaluator.Evaluate(candidate),
                        VerificationOutput = result.CombinedOutput,
                        Evidence =
                        [
                            $"Verification exit code: {result.ExitCode}",
                            $"Screenshot artifact: {candidate.RelativePath}"
                        ]
                    },
                    ct));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                reviews.Add(new ScreenshotLlmVisionReviewResult
                {
                    RelativePath = candidate.RelativePath,
                    Status = ScreenshotLlmVisionReviewStatus.Unknown,
                    Summary = $"LLM vision review could not complete: {ex.Message}"
                });
            }
        }

        return evidenceBuilder.BuildEvidence(reviews);
    }
}
