namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationWorkflowResult
{
    public required AgentVerificationPlan Plan { get; init; }

    public VerificationRunResult? RunResult { get; init; }

    public VerificationFailureAnalysis? FailureAnalysis { get; init; }

    public VerificationResultCard? ResultCard { get; init; }

    public required AgentRunState RunState { get; init; }

    public required string RunStepTitle { get; init; }

    public string RunStepDetail { get; init; } = string.Empty;

    public required string StatusText { get; init; }

    public required string LogText { get; init; }

    public string FailureSummary { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public bool HasFailure => FailureAnalysis != null;
}
