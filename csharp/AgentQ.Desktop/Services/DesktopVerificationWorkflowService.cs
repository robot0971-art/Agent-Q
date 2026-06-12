using AgentQ.Core.Providers;

namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationWorkflowService(
    DesktopVerificationRunner runner,
    VerificationFailureClassifier classifier,
    VerificationArtifactEvidenceBuilder artifactEvidenceBuilder,
    DesktopScreenshotLlmVisionWorkflowService screenshotLlmVisionWorkflowService)
{
    public async Task<DesktopVerificationWorkflowResult> RunAsync(
        AgentVerificationPlan plan,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands,
        TimeSpan timeout,
        CancellationToken ct,
        ProviderConfiguration? providerConfiguration = null)
    {
        try
        {
            var result = await runner.RunAsync(plan, workspaceRoot, timeout, projectAllowedCommands, ct);
            var summary = BuildSummary(result, workspaceRoot);

            if (result.Succeeded)
            {
                return new DesktopVerificationWorkflowResult
                {
                    Plan = plan,
                    RunResult = result,
                    ResultCard = VerificationResultCard.Passed(plan, result, summary),
                    RunState = AgentRunState.Done,
                    RunStepTitle = "Verification passed",
                    RunStepDetail = summary,
                    StatusText = "Verification passed",
                    LogText = summary,
                    Succeeded = true
                };
            }

            var analysis = classifier.Analyze(plan, result, workspaceRoot);
            var llmVisionEvidence = await screenshotLlmVisionWorkflowService.BuildEvidenceAsync(
                result,
                workspaceRoot,
                providerConfiguration,
                ct);
            if (llmVisionEvidence.Count > 0)
            {
                analysis = AppendEvidence(analysis, llmVisionEvidence);
            }

            return new DesktopVerificationWorkflowResult
            {
                Plan = plan,
                RunResult = result,
                FailureAnalysis = analysis,
                ResultCard = VerificationResultCard.Failed(plan, result, analysis, summary),
                RunState = AgentRunState.Failed,
                RunStepTitle = $"Verification failed: {analysis.Title}",
                RunStepDetail = $"{analysis.DisplayText}{Environment.NewLine}{summary}",
                StatusText = "Verification failed",
                LogText = summary,
                FailureSummary = summary
            };
        }
        catch (OperationCanceledException)
        {
            const string message = "Verification was cancelled by the user.";
            var analysis = classifier.AnalyzeException(new OperationCanceledException(message));
            return new DesktopVerificationWorkflowResult
            {
                Plan = plan,
                FailureAnalysis = analysis,
                ResultCard = VerificationResultCard.Warning(plan, analysis, message),
                RunState = AgentRunState.Cancelled,
                RunStepTitle = "Verification cancelled",
                RunStepDetail = plan.Command ?? string.Empty,
                StatusText = "Verification cancelled",
                LogText = "Verification cancelled",
                FailureSummary = message
            };
        }
        catch (Exception ex)
        {
            var analysis = classifier.AnalyzeException(ex);
            return new DesktopVerificationWorkflowResult
            {
                Plan = plan,
                FailureAnalysis = analysis,
                ResultCard = VerificationResultCard.Warning(plan, analysis, ex.Message),
                RunState = AgentRunState.Failed,
                RunStepTitle = $"Verification failed: {analysis.Title}",
                RunStepDetail = analysis.DisplayText,
                StatusText = $"Verification failed: {ex.Message}",
                LogText = $"Verification failed: {ex.Message}",
                FailureSummary = ex.Message
            };
        }
    }

    private string BuildSummary(VerificationRunResult result, string workspaceRoot)
    {
        var summary = string.IsNullOrWhiteSpace(result.CombinedOutput)
            ? $"Exit code: {result.ExitCode}"
            : $"Exit code: {result.ExitCode} - {TrimForLog(result.CombinedOutput)}";

        if (result.Artifacts.Count == 0)
        {
            return summary;
        }

        return $"{summary} | Artifacts: {artifactEvidenceBuilder.BuildSummary(result.Artifacts, workspaceRoot)}";
    }

    private static string TrimForLog(string value)
    {
        value = SensitiveTextRedactor.Redact(value.ReplaceLineEndings(" "));
        return value.Length <= 180 ? value : value[..180] + "...";
    }

    private static VerificationFailureAnalysis AppendEvidence(
        VerificationFailureAnalysis analysis,
        IReadOnlyList<string> evidence)
    {
        return new VerificationFailureAnalysis
        {
            Kind = analysis.Kind,
            Title = analysis.Title,
            Summary = analysis.Summary,
            SuggestedNextStep = analysis.SuggestedNextStep,
            Evidence = analysis.Evidence.Concat(evidence).Take(12).ToList()
        };
    }
}
