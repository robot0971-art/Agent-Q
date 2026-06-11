using System.Text.Json;
using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopVerificationCommandService(
    DesktopVerificationPanelWorkflowService verificationPanelWorkflowService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService,
    DesktopAgentRunWorkflowService agentRunWorkflowService,
    DesktopAutoFixWorkflowService autoFixWorkflowService)
{
    public async Task<DesktopVerificationWorkflowResult?> RunVerificationPlanAsync(
        MainViewModel viewModel,
        AgentVerificationPlan plan)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return null;
        }

        viewModel.IsBusy = true;
        var operationCts = new CancellationTokenSource();
        agentRunWorkflowService.SetActiveOperation(operationCts);

        try
        {
            var blockedResult = BuildBlockedVerificationResult(viewModel, plan);
            if (blockedResult != null)
            {
                verificationPanelWorkflowService.ApplyResult(viewModel, blockedResult);
                return blockedResult;
            }

            return await verificationPanelWorkflowService.RunVerificationAsync(
                viewModel,
                plan,
                workspaceContextWorkflowService.ProjectConfig?.VerificationCommands,
                TimeSpan.FromMinutes(2),
                viewModel.ToConfiguration(),
                operationCts.Token);
        }
        finally
        {
            agentRunWorkflowService.ClearActiveOperation(operationCts);
            operationCts.Dispose();
            viewModel.IsBusy = false;
        }
    }

    private static DesktopVerificationWorkflowResult? BuildBlockedVerificationResult(
        MainViewModel viewModel,
        AgentVerificationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Command))
        {
            return null;
        }

        var inputJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["command"] = plan.Command
        });
        var policy = ToolPermissionPolicy.Evaluate(
            "bash",
            inputJson,
            viewModel.WorkspaceRoot,
            viewModel.WorkMode);
        if (!policy.IsBlocked)
        {
            return null;
        }

        var detail = string.Join(
            " ",
            new[] { policy.PolicyReason, policy.Assessment.Reason }
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .Trim();
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = "The verification command was blocked by desktop policy.";
        }

        var analysis = new VerificationFailureAnalysis
        {
            Kind = VerificationFailureKind.PermissionBlocked,
            Title = "Verification command blocked",
            Summary = detail,
            SuggestedNextStep = "Use a focused build or test command that is allowed by the current work mode.",
            Evidence = [policy.Assessment.Summary]
        };

        return new DesktopVerificationWorkflowResult
        {
            Plan = plan,
            FailureAnalysis = analysis,
            ResultCard = VerificationResultCard.Warning(plan, analysis, detail),
            RunState = AgentRunState.Failed,
            RunStepTitle = "Verification blocked by policy",
            RunStepDetail = detail,
            StatusText = "Verification blocked",
            LogText = $"Verification blocked: {plan.Command}",
            FailureSummary = detail
        };
    }

    public async Task FixLastFailureAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var fixPrompt = verificationPanelWorkflowService.BuildFixPrompt();
        if (string.IsNullOrWhiteSpace(fixPrompt))
        {
            viewModel.StatusText = "No failed verification to fix";
            return;
        }

        if (!DesktopGeneratedPromptGuard.TryReplaceInput(viewModel, fixPrompt, "verification fix"))
        {
            return;
        }

        await sendCurrentMessageAsync(true);
    }

    public async Task AutoFixLastFailureAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        await autoFixWorkflowService.RunAsync(viewModel, maxAttempts: 3, sendCurrentMessageAsync);
    }

    public async Task ApprovePendingChangesAndVerifyAsync(
        MainViewModel viewModel,
        Func<bool, Task> sendCurrentMessageAsync)
    {
        await autoFixWorkflowService.ApprovePendingChangesAndVerifyAsync(
            viewModel,
            plan => RunVerificationPlanAsync(viewModel, plan),
            sendCurrentMessageAsync);
    }
}
