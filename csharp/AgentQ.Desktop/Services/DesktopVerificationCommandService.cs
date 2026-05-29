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

        viewModel.InputText = fixPrompt;
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
