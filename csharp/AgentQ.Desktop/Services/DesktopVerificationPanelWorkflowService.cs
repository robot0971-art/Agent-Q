using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationPanelWorkflowService(
    DesktopVerificationWorkflowService verificationWorkflowService)
{
    private AgentVerificationPlan? _lastFailedVerificationPlan;
    private VerificationRunResult? _lastFailedVerificationResult;
    private VerificationFailureAnalysis? _lastVerificationFailureAnalysis;

    public bool HasFailedVerification => _lastFailedVerificationPlan != null;

    public string LastFailureSignature { get; private set; } = string.Empty;

    public AgentVerificationPlan? CreateRetryPlan()
    {
        return _lastFailedVerificationPlan == null ||
               string.IsNullOrWhiteSpace(_lastFailedVerificationPlan.Command)
            ? null
            : new AgentVerificationPlan
            {
                Title = "Retry last verification",
                Command = _lastFailedVerificationPlan.Command,
                Reason = "Re-run the verification that failed before the fix attempt."
            };
    }

    public async Task<DesktopVerificationWorkflowResult> RunVerificationAsync(
        MainViewModel viewModel,
        AgentVerificationPlan plan,
        IEnumerable<string>? projectAllowedCommands,
        TimeSpan timeout,
        AgentQ.Core.Providers.ProviderConfiguration? providerConfiguration,
        CancellationToken ct)
    {
        viewModel.AddRunStep(AgentRunState.Verifying, $"Running verification: {plan.Title}", plan.Command);
        viewModel.AddLog($"Verification started: {plan.Command}");
        viewModel.StatusText = "Running verification";

        var result = await verificationWorkflowService.RunAsync(
            plan,
            viewModel.WorkspaceRoot,
            projectAllowedCommands,
            timeout,
            ct,
            providerConfiguration);

        ApplyResult(viewModel, result);
        return result;
    }

    public string? BuildFixPrompt()
    {
        return _lastFailedVerificationPlan == null
            ? null
            : DesktopPromptBuilder.BuildVerificationFixPrompt(
                _lastFailedVerificationPlan,
                _lastFailedVerificationResult,
                _lastVerificationFailureAnalysis);
    }

    public void ClearFailure(MainViewModel viewModel)
    {
        _lastFailedVerificationPlan = null;
        _lastFailedVerificationResult = null;
        _lastVerificationFailureAnalysis = null;
        LastFailureSignature = string.Empty;
        viewModel.ClearLastVerificationFailure();
    }

    public void RestoreRetryPlan(MainViewModel viewModel)
    {
        var retryPlan = CreateRetryPlan();
        if (retryPlan == null)
        {
            return;
        }

        viewModel.VerificationPlans.Add(retryPlan);
        viewModel.CanFixLastVerificationFailure = false;
    }

    private void ApplyResult(MainViewModel viewModel, DesktopVerificationWorkflowResult result)
    {
        if (result.HasFailure && result.FailureAnalysis != null)
        {
            _lastFailedVerificationPlan = result.Plan;
            _lastFailedVerificationResult = result.RunResult;
            _lastVerificationFailureAnalysis = result.FailureAnalysis;
            LastFailureSignature = BuildFailureSignature(result);
            viewModel.SetLastVerificationFailure($"{result.FailureAnalysis.Title}: {result.FailureSummary}");
        }
        else
        {
            ClearFailure(viewModel);
        }

        if (result.ResultCard != null)
        {
            viewModel.AddVerificationResult(result.ResultCard);
        }

        viewModel.AddRunStep(result.RunState, result.RunStepTitle, result.RunStepDetail);
        viewModel.StatusText = result.StatusText;
        viewModel.AddLog(result.LogText);
    }

    private static string BuildFailureSignature(DesktopVerificationWorkflowResult result)
    {
        var title = result.FailureAnalysis?.Title ?? "Unknown";
        var summary = NormalizeForSignature(result.FailureSummary);
        return $"{title}|{summary}";
    }

    private static string NormalizeForSignature(string value)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        while (value.Contains("  ", StringComparison.Ordinal))
        {
            value = value.Replace("  ", " ", StringComparison.Ordinal);
        }

        return value.Length <= 240 ? value : value[..240];
    }
}
