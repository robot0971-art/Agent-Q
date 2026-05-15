using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopCheckpointWorkflowService(
    AgentCheckpointService checkpointService,
    DesktopGitService gitService)
{
    public async Task<AgentCheckpoint?> LoadLatestAsync(string workspaceRoot, CancellationToken ct = default)
    {
        return await checkpointService.LoadLatestAsync(workspaceRoot, ct);
    }

    public async Task<AgentCheckpoint> SaveAsync(
        string workspaceRoot,
        string statusText,
        string pendingInput,
        IEnumerable<ChatMessageViewModel> messages,
        IEnumerable<string> logs,
        IEnumerable<AgentRunStep> runSteps,
        IEnumerable<AgentPlanItem> planItems,
        CancellationToken ct = default)
    {
        var checkpoint = await BuildCheckpointAsync(
            workspaceRoot,
            statusText,
            pendingInput,
            messages,
            logs,
            runSteps,
            planItems,
            ct);

        await checkpointService.SaveAsync(checkpoint, ct);
        return checkpoint;
    }

    public async Task<string?> PrepareResumePromptAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var checkpoint = await LoadLatestAsync(workspaceRoot, ct);
        return checkpoint == null
            ? null
            : DesktopPromptBuilder.BuildResumePrompt(checkpoint);
    }

    private async Task<AgentCheckpoint> BuildCheckpointAsync(
        string workspaceRoot,
        string statusText,
        string pendingInput,
        IEnumerable<ChatMessageViewModel> messages,
        IEnumerable<string> logs,
        IEnumerable<AgentRunStep> runSteps,
        IEnumerable<AgentPlanItem> planItems,
        CancellationToken ct)
    {
        var gitStatus = await gitService.GetStatusAsync(workspaceRoot, ct);
        var gitDiffStat = await gitService.GetDiffStatAsync(workspaceRoot, ct);

        return new AgentCheckpoint
        {
            WorkspaceRoot = workspaceRoot,
            StatusText = statusText,
            PendingInput = pendingInput,
            GitStatus = gitStatus.DisplayOutput,
            GitDiffStat = gitDiffStat.DisplayOutput,
            Conversation = messages
                .TakeLast(20)
                .Select(message => new AgentCheckpointMessage
                {
                    Role = message.Role,
                    Content = DesktopPromptBuilder.Truncate(message.Content, 6000),
                    CreatedAt = message.CreatedAt
                })
                .ToList(),
            Logs = logs.TakeLast(80).ToList(),
            RunSteps = runSteps.TakeLast(40).Select(step => new AgentCheckpointRunStep
            {
                State = step.StateText,
                Title = step.Title,
                Detail = DesktopPromptBuilder.Truncate(step.Detail, 2000),
                CreatedAt = step.CreatedAt
            }).ToList(),
            PlanItems = planItems.Select(item => new AgentCheckpointPlanItem
            {
                Order = item.Order,
                Title = item.Title,
                Detail = item.Detail,
                Status = item.Status
            }).ToList()
        };
    }
}
