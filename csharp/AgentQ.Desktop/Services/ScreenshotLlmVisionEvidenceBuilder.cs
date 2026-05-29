namespace AgentQ.Desktop.Services;

public sealed class ScreenshotLlmVisionEvidenceBuilder
{
    public IReadOnlyList<string> BuildEvidence(IReadOnlyList<ScreenshotLlmVisionReviewResult> results)
    {
        return results.Select(BuildEvidence).ToList();
    }

    public string BuildEvidence(ScreenshotLlmVisionReviewResult result)
    {
        var findings = result.Findings.Count == 0
            ? string.Empty
            : " Findings: " + string.Join("; ", result.Findings.Take(3));

        return $"Screenshot LLM vision review {result.Status}: {result.RelativePath}. {result.Summary}{findings}";
    }
}
