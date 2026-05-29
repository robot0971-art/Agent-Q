namespace AgentQ.Desktop.Services;

public sealed class VerificationResultCard
{
    public required string Status { get; init; }

    public required string Title { get; init; }

    public string Command { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string OutputPreview { get; init; } = string.Empty;

    public string VisualEvidenceSummary { get; init; } = string.Empty;

    public bool HasVisualEvidence => !string.IsNullOrWhiteSpace(VisualEvidenceSummary);

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public string CreatedAtText => CreatedAt.ToString("HH:mm:ss");

    public string AccentBrush { get; init; } = "#64748B";

    public string BadgeBackground { get; init; } = "#1E293B";

    public static VerificationResultCard Passed(AgentVerificationPlan plan, VerificationRunResult result, string summary)
    {
        return new VerificationResultCard
        {
            Status = "PASSED",
            Title = plan.Title,
            Command = plan.Command ?? string.Empty,
            Summary = summary,
            Detail = "Verification completed successfully.",
            OutputPreview = BuildOutputPreview(result.CombinedOutput),
            AccentBrush = "#22C55E",
            BadgeBackground = "#123524"
        };
    }

    public static VerificationResultCard Failed(
        AgentVerificationPlan plan,
        VerificationRunResult? result,
        VerificationFailureAnalysis analysis,
        string summary)
    {
        return new VerificationResultCard
        {
            Status = "FAILED",
            Title = analysis.Title,
            Command = plan.Command ?? string.Empty,
            Summary = summary,
            Detail = analysis.Summary,
            OutputPreview = BuildOutputPreview(result?.CombinedOutput ?? string.Join(Environment.NewLine, analysis.Evidence)),
            VisualEvidenceSummary = BuildVisualEvidenceSummary(analysis.Evidence),
            AccentBrush = "#EF4444",
            BadgeBackground = "#3A1518"
        };
    }

    public static VerificationResultCard Warning(AgentVerificationPlan plan, VerificationFailureAnalysis analysis, string summary)
    {
        return new VerificationResultCard
        {
            Status = "WARNING",
            Title = analysis.Title,
            Command = plan.Command ?? string.Empty,
            Summary = summary,
            Detail = analysis.Summary,
            OutputPreview = BuildOutputPreview(string.Join(Environment.NewLine, analysis.Evidence)),
            VisualEvidenceSummary = BuildVisualEvidenceSummary(analysis.Evidence),
            AccentBrush = "#F59E0B",
            BadgeBackground = "#3A2A10"
        };
    }

    private static string BuildOutputPreview(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "No output.";
        }

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Take(5);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildVisualEvidenceSummary(IReadOnlyList<string> evidence)
    {
        var items = evidence
            .Where(item => item.Contains("Screenshot LLM vision review", StringComparison.OrdinalIgnoreCase) ||
                           item.Contains("Screenshot visual review", StringComparison.OrdinalIgnoreCase) ||
                           item.Contains("Screenshot quality", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        return items.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, items);
    }
}
