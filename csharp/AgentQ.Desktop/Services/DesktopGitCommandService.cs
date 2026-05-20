using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopGitCommandService(DesktopGitPanelWorkflowService gitPanelWorkflowService)
{
    public async Task RefreshStatusAsync(
        MainViewModel viewModel,
        Func<string, string> trimForLog,
        CancellationToken ct = default)
    {
        await gitPanelWorkflowService.RefreshStatusAsync(viewModel, trimForLog, ct);
    }

    public async Task RefreshDiffAsync(
        MainViewModel viewModel,
        Func<string, string> trimForLog,
        CancellationToken ct = default)
    {
        await gitPanelWorkflowService.RefreshDiffAsync(viewModel, trimForLog, ct);
    }

    public async Task ReviewChangesAsync(
        MainViewModel viewModel,
        Func<bool, Task> sendCurrentMessageAsync,
        Func<string, string> trimForLog,
        CancellationToken ct = default)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        viewModel.StatusText = "Preparing code review";

        var result = await gitPanelWorkflowService.PrepareCodeReviewAsync(viewModel.WorkspaceRoot, ct);
        if (!gitPanelWorkflowService.ApplyPromptResult(viewModel, result, trimForLog))
        {
            return;
        }

        var messageCountBeforeReview = viewModel.Messages.Count;
        viewModel.InputText = result.Prompt;
        await sendCurrentMessageAsync(false);
        gitPanelWorkflowService.CaptureLastCodeReview(viewModel, messageCountBeforeReview);
    }

    public async Task FixCodeReviewFindingsAsync(
        MainViewModel viewModel,
        Func<bool, Task> sendCurrentMessageAsync,
        Func<string, string> trimForLog,
        CancellationToken ct = default)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (string.IsNullOrWhiteSpace(gitPanelWorkflowService.LastCodeReviewText))
        {
            viewModel.StatusText = "No code review to fix";
            return;
        }

        var result = await gitPanelWorkflowService.PrepareCodeReviewFixAsync(viewModel.WorkspaceRoot, ct);
        if (!gitPanelWorkflowService.ApplyPromptResult(viewModel, result, trimForLog))
        {
            return;
        }

        viewModel.InputText = result.Prompt;
        viewModel.CanFixLastCodeReviewFindings = false;
        await sendCurrentMessageAsync(false);
    }

    public async Task PrepareCommitSummaryAsync(
        MainViewModel viewModel,
        Func<bool, Task> sendCurrentMessageAsync,
        Func<string, string> trimForLog,
        CancellationToken ct = default)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        viewModel.StatusText = "Preparing commit summary";

        var result = await gitPanelWorkflowService.PrepareCommitSummaryAsync(viewModel.WorkspaceRoot, ct);
        if (!gitPanelWorkflowService.ApplyPromptResult(viewModel, result, trimForLog))
        {
            return;
        }

        viewModel.InputText = result.Prompt;
        await sendCurrentMessageAsync(false);
    }
}
