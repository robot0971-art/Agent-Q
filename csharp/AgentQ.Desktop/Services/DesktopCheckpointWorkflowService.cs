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
            Conversation = BuildCheckpointConversation(messages, runSteps),
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

    private static List<AgentCheckpointMessage> BuildCheckpointConversation(
        IEnumerable<ChatMessageViewModel> messages,
        IEnumerable<AgentRunStep> runSteps)
    {
        var hasRecordedFileChange = runSteps.Any(step =>
            step.State == AgentRunState.RecordingChanges ||
            step.Title.Contains("file changed", StringComparison.OrdinalIgnoreCase) ||
            step.Detail.Contains("file changed", StringComparison.OrdinalIgnoreCase));
        var note = hasRecordedFileChange
            ? "Checkpoint note: workspace file changes were recorded in this run; ignore the omitted off-target assistant text and inspect current files/run steps before resuming."
            : "Checkpoint note: off-target assistant text was omitted; inspect current files, run steps, and the latest user request before resuming.";

        return messages
            .TakeLast(20)
            .Select(message =>
            {
                var content = message.Content;
                if (string.Equals(message.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
                    LooksLikeIrrelevantAssistantText(content))
                {
                    content = note;
                }

                return new AgentCheckpointMessage
                {
                    Role = message.Role,
                    Content = DesktopPromptBuilder.Truncate(content, 6000),
                    CreatedAt = message.CreatedAt
                };
            })
            .ToList();
    }

    private static bool LooksLikeIrrelevantAssistantText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var mentionsReadingAndGames =
            value.Contains("\uB3C5\uC11C", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("\uAC8C\uC784", StringComparison.OrdinalIgnoreCase);
        var mentionsMojibakeReadingOrGames =
            value.Contains("\u003F\uB086\uAF4C", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("\u5BC3\uB6AF\uC5EB", StringComparison.OrdinalIgnoreCase);

        if (mentionsReadingAndGames || mentionsMojibakeReadingOrGames)
        {
            return true;
        }

        return value.Contains("\uBB34\uC5C7\uC744 \uB3C4\uC640", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("what can I help", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("reading", StringComparison.OrdinalIgnoreCase) && value.Contains("games", StringComparison.OrdinalIgnoreCase);
    }
}
