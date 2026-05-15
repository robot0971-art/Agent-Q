namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationWorkflowService(
    DesktopVerificationRunner runner,
    VerificationFailureClassifier classifier)
{
    public async Task<DesktopVerificationWorkflowResult> RunAsync(
        AgentVerificationPlan plan,
        string workspaceRoot,
        IEnumerable<string>? projectAllowedCommands,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(plan, workspaceRoot, timeout, projectAllowedCommands, ct);
            var summary = BuildSummary(result);

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

            var analysis = classifier.Analyze(plan, result);
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

    private static string BuildSummary(VerificationRunResult result)
    {
        return string.IsNullOrWhiteSpace(result.CombinedOutput)
            ? $"Exit code: {result.ExitCode}"
            : $"Exit code: {result.ExitCode} - {TrimForLog(result.CombinedOutput)}";
    }

    private static string TrimForLog(string value)
    {
        value = value.ReplaceLineEndings(" ");
        return value.Length <= 180 ? value : value[..180] + "...";
    }
}
