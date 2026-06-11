using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopPlanCheckpointWorkflowService(
    DesktopPlanWorkflowService planWorkflowService,
    DesktopCheckpointWorkflowService checkpointWorkflowService,
    DesktopPlanApprovalPreviewService approvalPreviewService)
{
    private AgentCheckpoint? _lastCheckpoint;

    public bool HasCheckpoint => _lastCheckpoint != null;

    public string? BuildPlanPrompt(MainViewModel viewModel)
    {
        var goal = string.IsNullOrWhiteSpace(viewModel.InputText)
            ? DesktopConversationSummaryBuilder.BuildRecentText(viewModel.Messages, maxMessages: 8)
            : viewModel.InputText.Trim();

        return string.IsNullOrWhiteSpace(goal)
            ? null
            : DesktopPromptBuilder.BuildPlannerPrompt(goal);
    }

    public bool CapturePlanItems(MainViewModel viewModel, int messageCountBeforePlan)
    {
        var planText = viewModel.Messages.Skip(messageCountBeforePlan).LastOrDefault(item =>
            string.Equals(item.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Content))?.Content;

        if (string.IsNullOrWhiteSpace(planText))
        {
            return false;
        }

        var items = planWorkflowService.ParsePlan(planText);
        if (items.Count == 0)
        {
            viewModel.StatusText = "Plan generated but no checklist items were parsed";
            viewModel.AddLog("Plan parser found no checklist items");
            return false;
        }

        planWorkflowService.ReplacePlanItems(viewModel.PlanItems, items);
        SelectNextOpenPlanItem(viewModel);
        approvalPreviewService.ApplyPreview(viewModel);
        viewModel.StatusText = $"Plan captured: {viewModel.PlanItems.Count} items";
        viewModel.AddLog($"Plan captured: {viewModel.PlanItems.Count} items");
        return true;
    }

    public AgentPlanItem? PrepareNextPlanItem(MainViewModel viewModel)
    {
        var item = viewModel.SelectedPlanItem ??
                   viewModel.PlanItems.FirstOrDefault(plan =>
                       plan.Status is AgentPlanItemStatus.Pending or AgentPlanItemStatus.InProgress);
        if (item == null)
        {
            viewModel.StatusText = "No plan item to continue";
            return null;
        }

        if (viewModel.CurrentWorkerExecutionContext?.State == WorkerExecutionState.Blocked)
        {
            viewModel.StatusText = "Plan is blocked by validation";
            viewModel.AddLog("Plan execution blocked: validation failed");
            return null;
        }

        if (viewModel.CurrentWorkerExecutionContext?.State == WorkerExecutionState.AwaitingApproval ||
            viewModel.HasPendingPlanApproval)
        {
            viewModel.StatusText = "Plan approval required before execution";
            viewModel.AddLog("Plan execution blocked: approval required");
            return null;
        }

        var prompt = DesktopPromptBuilder.BuildContinuePlanItemPrompt(item, viewModel.PlanItems);
        if (!string.IsNullOrWhiteSpace(viewModel.InputText) &&
            !string.Equals(viewModel.InputText.Trim(), prompt.Trim(), StringComparison.Ordinal))
        {
            viewModel.StatusText = "Send or clear the current draft before continuing the plan";
            viewModel.AddLog("Plan continuation blocked because the input box contains a user draft.");
            return null;
        }

        item.Status = AgentPlanItemStatus.InProgress;
        viewModel.SelectedPlanItem = item;
        viewModel.InputText = prompt;
        viewModel.AddLog($"Continuing plan item: {item.Title}");
        return item;
    }

    public bool MarkSelectedPlanItemDone(MainViewModel viewModel)
    {
        var item = viewModel.SelectedPlanItem;
        if (item == null)
        {
            viewModel.StatusText = "No selected plan item";
            return false;
        }

        item.Status = AgentPlanItemStatus.Done;
        SelectNextOpenPlanItem(viewModel);
        viewModel.StatusText = viewModel.SelectedPlanItem == null
            ? "Plan item marked done; plan complete"
            : "Plan item marked done; next item selected";
        viewModel.AddLog($"Plan item done: {item.Title}");
        return true;
    }

    public async Task LoadLatestCheckpointAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        _lastCheckpoint = await checkpointWorkflowService.LoadLatestAsync(viewModel.WorkspaceRoot, ct);
        if (_lastCheckpoint == null)
        {
            viewModel.LatestCheckpointText = "No checkpoint loaded.";
            viewModel.CanResumeCheckpoint = false;
            return;
        }

        viewModel.LatestCheckpointText = DesktopPromptBuilder.BuildCheckpointDisplayText(_lastCheckpoint);
        ApplyCheckpointPlanItems(viewModel, _lastCheckpoint);
        viewModel.CanResumeCheckpoint = true;
        viewModel.AddLog($"Checkpoint loaded: {_lastCheckpoint.CreatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    public async Task SaveCheckpointAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        var checkpoint = await checkpointWorkflowService.SaveAsync(
            viewModel.WorkspaceRoot,
            viewModel.StatusText,
            viewModel.InputText,
            viewModel.Messages,
            viewModel.Logs,
            viewModel.RunSteps,
            viewModel.PlanItems,
            ct);
        _lastCheckpoint = checkpoint;
        viewModel.LatestCheckpointText = DesktopPromptBuilder.BuildCheckpointDisplayText(checkpoint);
        viewModel.CanResumeCheckpoint = true;
        viewModel.StatusText = "Checkpoint saved";
        viewModel.AddLog($"Checkpoint saved: {checkpoint.CreatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    public async Task<string?> BuildResumeCheckpointPromptAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        _lastCheckpoint ??= await checkpointWorkflowService.LoadLatestAsync(viewModel.WorkspaceRoot, ct);
        if (_lastCheckpoint == null)
        {
            viewModel.StatusText = "No checkpoint to resume";
            viewModel.CanResumeCheckpoint = false;
            return null;
        }

        viewModel.AddLog("Resume prompt prepared");
        return DesktopPromptBuilder.BuildResumePrompt(_lastCheckpoint);
    }

    public void SelectNextOpenPlanItem(MainViewModel viewModel)
    {
        viewModel.SelectedPlanItem = planWorkflowService.SelectNextOpen(viewModel.PlanItems);
    }

    private void ApplyCheckpointPlanItems(MainViewModel viewModel, AgentCheckpoint checkpoint)
    {
        planWorkflowService.ApplyCheckpoint(viewModel.PlanItems, checkpoint);
        SelectNextOpenPlanItem(viewModel);
        approvalPreviewService.ApplyPreview(viewModel);
    }
}
