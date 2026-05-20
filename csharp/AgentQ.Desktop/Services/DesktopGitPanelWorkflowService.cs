using AgentQ.Desktop.ViewModels;

namespace AgentQ.Desktop.Services;

public sealed class DesktopGitPanelWorkflowService(DesktopGitService gitService)
{
    private readonly DesktopGitWorkflowService _gitWorkflowService = new(gitService);
    private readonly Dictionary<string, GitChangeReviewStatus> _reviewStatuses = new(StringComparer.OrdinalIgnoreCase);
    private string _lastCodeReviewText = string.Empty;

    public string LastCodeReviewText => _lastCodeReviewText;

    public async Task<DesktopGitPromptResult> PrepareCodeReviewAsync(string workspaceRoot, CancellationToken ct = default)
    {
        return await _gitWorkflowService.PrepareCodeReviewAsync(workspaceRoot, ct);
    }

    public async Task<DesktopGitPromptResult> PrepareCodeReviewFixAsync(string workspaceRoot, CancellationToken ct = default)
    {
        return await _gitWorkflowService.PrepareCodeReviewFixAsync(workspaceRoot, _lastCodeReviewText, ct);
    }

    public async Task<DesktopGitPromptResult> PrepareCommitSummaryAsync(string workspaceRoot, CancellationToken ct = default)
    {
        return await _gitWorkflowService.PrepareCommitSummaryAsync(workspaceRoot, ct);
    }

    public async Task RefreshStatusAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = "Refreshing git status";
        var snapshot = await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct);
        ApplySnapshot(viewModel, snapshot);
        viewModel.StatusText = snapshot.Status.Succeeded ? "Git status refreshed" : "Git status failed";
        viewModel.AddLog(snapshot.Status.Succeeded
            ? "Git status refreshed"
            : $"Git status failed: {trimForLog(snapshot.Status.DisplayOutput)}");
    }

    public async Task RefreshDiffAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = "Refreshing git diff";
        var result = await gitService.GetDiffStatAsync(viewModel.WorkspaceRoot, ct);
        viewModel.GitDiffText = result.DisplayOutput;
        viewModel.GitLastUpdatedText = CurrentTimestamp();
        viewModel.StatusText = result.Succeeded ? "Git diff refreshed" : "Git diff failed";
        viewModel.AddLog(result.Succeeded
            ? "Git diff refreshed"
            : $"Git diff failed: {trimForLog(result.DisplayOutput)}");
    }

    public async Task LoadSelectedFileDiffAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        if (viewModel.SelectedGitChangedFile == null)
        {
            viewModel.GitSelectedFileDiffText = "Select a changed file to view its diff.";
            return;
        }

        viewModel.StatusText = "Loading file diff";
        var result = await gitService.GetFileDiffAsync(viewModel.WorkspaceRoot, viewModel.SelectedGitChangedFile, ct);
        viewModel.GitSelectedFileDiffText = result.DisplayOutput;
        viewModel.GitLastUpdatedText = CurrentTimestamp();
        viewModel.StatusText = result.Succeeded ? "File diff loaded" : "File diff failed";
    }

    public async Task StageSelectedFileAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = "No selected changed file";
            return;
        }

        viewModel.StatusText = "Staging selected file";
        var result = await gitService.StageFileAsync(viewModel.WorkspaceRoot, file, ct);
        await ApplyGitMutationResultAsync(viewModel, result, "Selected file staged", "Stage selected failed", trimForLog, ct);
    }

    public async Task StageApprovedFilesAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var files = viewModel.GitChangedFiles
            .Where(file => file.ReviewStatus == GitChangeReviewStatus.Approved)
            .ToArray();

        viewModel.StatusText = "Staging approved files";
        var result = await gitService.StageFilesAsync(viewModel.WorkspaceRoot, files, ct);
        await ApplyGitMutationResultAsync(viewModel, result, "Approved files staged", "Stage approved failed", trimForLog, ct);
    }

    public async Task UnstageSelectedFileAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = "No selected changed file";
            return;
        }

        viewModel.StatusText = "Unstaging selected file";
        var result = await gitService.UnstageFileAsync(viewModel.WorkspaceRoot, file, ct);
        await ApplyGitMutationResultAsync(viewModel, result, "Selected file unstaged", "Unstage selected failed", trimForLog, ct);
    }

    public async Task CommitAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewModel.GitCommitMessage))
        {
            viewModel.StatusText = "Commit message is required";
            return;
        }

        if (viewModel.GitChangedFiles.All(file => !file.IsStaged))
        {
            viewModel.StatusText = "No staged files to commit";
            return;
        }

        viewModel.StatusText = "Creating commit";
        var result = await gitService.CommitAsync(viewModel.WorkspaceRoot, viewModel.GitCommitMessage.Trim(), ct);
        if (result.Succeeded)
        {
            viewModel.GitCommitMessage = string.Empty;
        }

        await ApplyGitMutationResultAsync(viewModel, result, "Commit created", "Commit failed", trimForLog, ct);
    }

    public async Task PullFastForwardOnlyAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = "Checking pull safety";
        var snapshot = await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct);
        ApplySnapshot(viewModel, snapshot);

        if (!snapshot.Status.Succeeded)
        {
            viewModel.StatusText = $"Pull unavailable: {trimForLog(snapshot.Status.DisplayOutput)}";
            return;
        }

        var safety = GitPullSafetyAnalyzer.Analyze(snapshot.Status.DisplayOutput, snapshot.ChangedFiles);
        if (!safety.CanPull)
        {
            viewModel.StatusText = $"Pull blocked: {safety.Reason}";
            viewModel.AddLog($"Pull blocked: {safety.Reason}");
            return;
        }

        viewModel.StatusText = "Pulling with fast-forward only";
        var result = await gitService.PullFastForwardOnlyAsync(viewModel.WorkspaceRoot, ct);
        await ApplyGitMutationResultAsync(viewModel, result, "Pull completed", "Pull failed", trimForLog, ct);
    }

    public bool ApplyPromptResult(MainViewModel viewModel, DesktopGitPromptResult result, Func<string, string> trimForLog)
    {
        ApplySnapshot(viewModel, result.Snapshot);
        if (!result.Snapshot.Succeeded)
        {
            viewModel.StatusText = result.FailureStatus;
            viewModel.AddLog($"{result.FailureLogPrefix}: {trimForLog(result.Snapshot.Status.DisplayOutput)}");
            return false;
        }

        if (!result.Snapshot.HasChanges)
        {
            viewModel.StatusText = result.NoChangesStatus;
            viewModel.AddLog(result.NoChangesLog);
            return false;
        }

        viewModel.AddLog(result.SuccessLog);
        return true;
    }

    public void ApplySnapshot(MainViewModel viewModel, DesktopGitSnapshot snapshot)
    {
        var selectedPath = viewModel.SelectedGitChangedFile?.Path;
        var branchSummary = GitBranchStatusAnalyzer.Analyze(snapshot.Status.DisplayOutput);
        viewModel.GitStatusText = string.IsNullOrWhiteSpace(branchSummary)
            ? snapshot.Status.DisplayOutput
            : $"{snapshot.Status.DisplayOutput}{Environment.NewLine}{Environment.NewLine}{branchSummary}";
        viewModel.GitDiffText = snapshot.DiffStat.DisplayOutput;
        ApplyChangedFiles(viewModel, snapshot.ChangedFiles, selectedPath);
        viewModel.GitLastUpdatedText = CurrentTimestamp();
    }

    public void SetSelectedReviewStatus(MainViewModel viewModel, GitChangeReviewStatus status)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = "No selected changed file";
            return;
        }

        file.ReviewStatus = status;
        _reviewStatuses[file.Path] = status;
        viewModel.StatusText = $"Change marked {status}";
        viewModel.AddLog($"Change review status: {status} - {file.Path}");
    }

    public void CaptureLastCodeReview(MainViewModel viewModel, int messageCountBeforeReview)
    {
        var review = viewModel.Messages.Skip(messageCountBeforeReview).LastOrDefault(item =>
            string.Equals(item.Role, "AgentQ", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Content))?.Content;

        if (string.IsNullOrWhiteSpace(review))
        {
            return;
        }

        _lastCodeReviewText = review;
        viewModel.CanFixLastCodeReviewFindings = true;
        viewModel.AddLog("Code review captured");
    }

    public void ClearLastCodeReview(MainViewModel viewModel)
    {
        _lastCodeReviewText = string.Empty;
        viewModel.CanFixLastCodeReviewFindings = false;
    }

    public void ClearPanel(MainViewModel viewModel)
    {
        viewModel.GitChangedFiles.Clear();
        viewModel.SelectedGitChangedFile = null;
        _reviewStatuses.Clear();
        viewModel.GitStatusText = "Not refreshed yet.";
        viewModel.GitDiffText = "Not refreshed yet.";
        viewModel.GitSelectedFileDiffText = "Select a changed file to view its diff.";
        viewModel.GitLastUpdatedText = "Git not refreshed yet.";
        viewModel.GitCommitMessage = string.Empty;
        ClearLastCodeReview(viewModel);
    }

    private async Task ApplyGitMutationResultAsync(
        MainViewModel viewModel,
        GitCommandResult result,
        string successStatus,
        string failureStatus,
        Func<string, string> trimForLog,
        CancellationToken ct)
    {
        if (result.Succeeded)
        {
            viewModel.StatusText = successStatus;
            viewModel.AddLog(successStatus);
            ApplySnapshot(viewModel, await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct));
            return;
        }

        viewModel.StatusText = $"{failureStatus}: {trimForLog(result.DisplayOutput)}";
        viewModel.AddLog($"{failureStatus}: {trimForLog(result.DisplayOutput)}");
    }

    private void ApplyChangedFiles(MainViewModel viewModel, IReadOnlyList<GitChangedFile> files, string? selectedPath)
    {
        viewModel.GitChangedFiles.Clear();
        foreach (var file in files)
        {
            if (_reviewStatuses.TryGetValue(file.Path, out var reviewStatus))
            {
                file.ReviewStatus = reviewStatus;
            }

            viewModel.GitChangedFiles.Add(file);
        }

        viewModel.SelectedGitChangedFile = viewModel.GitChangedFiles.FirstOrDefault(file =>
            string.Equals(file.Path, selectedPath, StringComparison.OrdinalIgnoreCase));

        if (viewModel.GitChangedFiles.Count == 0)
        {
            viewModel.GitSelectedFileDiffText = "No changed files.";
        }
    }

    private static string CurrentTimestamp()
    {
        return $"Last updated: {DateTime.Now:HH:mm:ss}";
    }
}
