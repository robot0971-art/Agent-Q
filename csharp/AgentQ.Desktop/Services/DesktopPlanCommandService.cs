using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanCommandService(
    DesktopPlanCheckpointWorkflowService planCheckpointWorkflowService,
    DesktopWorkspaceContextWorkflowService workspaceContextWorkflowService)
{
    public async Task CreatePlanAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = planCheckpointWorkflowService.BuildPlanPrompt(viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            viewModel.StatusText = "No goal to plan";
            return;
        }

        viewModel.InputText = planPrompt;
        viewModel.AddLog("Plan prompt prepared");
        var messageCountBeforePlan = viewModel.Messages.Count;
        await sendCurrentMessageAsync(false);
        planCheckpointWorkflowService.CapturePlanItems(viewModel, messageCountBeforePlan);
    }

    public async Task ContinueNextPlanItemAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        if (planCheckpointWorkflowService.PrepareNextPlanItem(viewModel) == null)
        {
            return;
        }

        await sendCurrentMessageAsync(false);
    }

    public async Task PlanAndRunAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var planPrompt = planCheckpointWorkflowService.BuildPlanPrompt(viewModel);
        if (string.IsNullOrWhiteSpace(planPrompt))
        {
            viewModel.StatusText = "No goal to plan";
            return;
        }

        viewModel.InputText = planPrompt;
        viewModel.AddLog("Plan+run prompt prepared");
        var messageCountBeforePlan = viewModel.Messages.Count;
        await sendCurrentMessageAsync(false);
        if (viewModel.IsBusy)
        {
            return;
        }

        planCheckpointWorkflowService.CapturePlanItems(viewModel, messageCountBeforePlan);
        if (viewModel.PlanItems.Count == 0)
        {
            return;
        }

        await ContinueNextPlanItemAsync(viewModel, sendCurrentMessageAsync);
    }

    public void MarkPlanItemDone(MainViewModel viewModel)
    {
        planCheckpointWorkflowService.MarkSelectedPlanItemDone(viewModel);
    }

    public async Task MarkDoneAndContinueAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        MarkPlanItemDone(viewModel);
        if (viewModel.SelectedPlanItem != null)
        {
            await ContinueNextPlanItemAsync(viewModel, sendCurrentMessageAsync);
        }
    }

    public async Task SaveCheckpointAsync(MainViewModel viewModel)
    {
        try
        {
            await planCheckpointWorkflowService.SaveCheckpointAsync(viewModel);
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Checkpoint save failed: {ex.Message}";
            viewModel.AddLog($"Checkpoint save failed: {ex.Message}");
        }
    }

    public async Task LoadCheckpointAsync(MainViewModel viewModel)
    {
        await planCheckpointWorkflowService.LoadLatestCheckpointAsync(viewModel);
        viewModel.StatusText = planCheckpointWorkflowService.HasCheckpoint ? "Checkpoint loaded" : "No checkpoint found";
    }

    public async Task ResumeCheckpointAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await planCheckpointWorkflowService.BuildResumeCheckpointPromptAsync(viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        viewModel.InputText = resumePrompt;
        await sendCurrentMessageAsync(false);
    }

    public async Task SaveSessionSummaryAsync(
        MainViewModel viewModel,
        Func<string, string> trimForLog)
    {
        await workspaceContextWorkflowService.SaveSessionSummaryAsync(
            viewModel,
            "Manual session summary saved",
            trimForLog);
    }

    public async Task LoadSessionSummaryAsync(MainViewModel viewModel)
    {
        await workspaceContextWorkflowService.LoadLatestSessionSummaryAsync(viewModel);
        viewModel.StatusText = workspaceContextWorkflowService.HasSessionSummary
            ? "Session summary loaded"
            : "No session summary found";
    }

    public async Task ResumeSessionSummaryAsync(MainViewModel viewModel, Func<bool, Task> sendCurrentMessageAsync)
    {
        if (viewModel.IsBusy)
        {
            viewModel.StatusText = "AgentQ is busy";
            return;
        }

        var resumePrompt = await workspaceContextWorkflowService.BuildResumeSessionSummaryPromptAsync(viewModel);
        if (string.IsNullOrWhiteSpace(resumePrompt))
        {
            return;
        }

        viewModel.InputText = resumePrompt;
        await sendCurrentMessageAsync(false);
    }
}
