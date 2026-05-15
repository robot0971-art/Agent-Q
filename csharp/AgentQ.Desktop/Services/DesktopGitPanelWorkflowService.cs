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
        viewModel.GitStatusText = snapshot.Status.DisplayOutput;
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
        ClearLastCodeReview(viewModel);
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
