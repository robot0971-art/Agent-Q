namespace AgentQ.Desktop.Services;

public sealed class DesktopGitWorkflowService(DesktopGitService gitService)
{
    public async Task<DesktopGitSnapshot> GetSnapshotAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var status = await gitService.GetStatusAsync(workspaceRoot, ct);
        var diffStat = await gitService.GetDiffStatAsync(workspaceRoot, ct);
        var fullDiff = await gitService.GetFullDiffAsync(workspaceRoot, ct);
        var changedFiles = await gitService.GetChangedFilesAsync(workspaceRoot, ct);

        return new DesktopGitSnapshot
        {
            Status = status,
            DiffStat = diffStat,
            FullDiff = fullDiff,
            ChangedFiles = changedFiles
        };
    }

    public async Task<DesktopGitPromptResult> PrepareCodeReviewAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(workspaceRoot, ct);
        return new DesktopGitPromptResult
        {
            Snapshot = snapshot,
            Prompt = snapshot.HasChanges
                ? DesktopPromptBuilder.BuildCodeReviewPrompt(snapshot.Status, snapshot.DiffStat, snapshot.FullDiff)
                : string.Empty,
            SuccessLog = "Code review prompt prepared",
            NoChangesStatus = "No changes to review",
            NoChangesLog = "Code review skipped: no git changes",
            FailureLogPrefix = "Code review unavailable"
        };
    }

    public async Task<DesktopGitPromptResult> PrepareCodeReviewFixAsync(
        string workspaceRoot,
        string review,
        CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(workspaceRoot, ct);
        return new DesktopGitPromptResult
        {
            Snapshot = snapshot,
            Prompt = DesktopPromptBuilder.BuildCodeReviewFixPrompt(review, snapshot.Status, snapshot.DiffStat, snapshot.FullDiff),
            SuccessLog = "Code review fix prompt prepared",
            FailureLogPrefix = "Code review fix unavailable"
        };
    }

    public async Task<DesktopGitPromptResult> PrepareCommitSummaryAsync(string workspaceRoot, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(workspaceRoot, ct);
        return new DesktopGitPromptResult
        {
            Snapshot = snapshot,
            Prompt = snapshot.HasChanges
                ? DesktopPromptBuilder.BuildCommitSummaryPrompt(snapshot.Status, snapshot.DiffStat, snapshot.FullDiff)
                : string.Empty,
            SuccessLog = "Commit summary prompt prepared",
            NoChangesStatus = "No changes to summarize",
            NoChangesLog = "Commit summary skipped: no git changes",
            FailureLogPrefix = "Commit summary unavailable"
        };
    }
}
