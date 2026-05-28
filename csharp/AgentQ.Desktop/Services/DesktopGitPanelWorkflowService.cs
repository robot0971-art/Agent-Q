using AgentQ.Desktop.ViewModels;
using System.Globalization;

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
        viewModel.StatusText = Ui(viewModel, DesktopText.GitRefreshingStatus);
        var snapshot = await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct);
        ApplySnapshot(viewModel, snapshot);
        viewModel.StatusText = snapshot.Status.Succeeded ? Ui(viewModel, DesktopText.GitStatusRefreshed) : Ui(viewModel, DesktopText.GitStatusFailed);
        viewModel.AddLog(snapshot.Status.Succeeded
            ? Ui(viewModel, DesktopText.GitStatusRefreshed)
            : $"{Ui(viewModel, DesktopText.GitStatusFailed)}: {trimForLog(snapshot.Status.DisplayOutput)}");
    }

    public async Task RefreshDiffAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = Ui(viewModel, DesktopText.GitRefreshingDiff);
        var result = await gitService.GetDiffStatAsync(viewModel.WorkspaceRoot, ct);
        viewModel.GitDiffText = result.DisplayOutput;
        viewModel.GitLastUpdatedText = CurrentTimestamp(viewModel);
        viewModel.StatusText = result.Succeeded ? Ui(viewModel, DesktopText.GitDiffRefreshed) : Ui(viewModel, DesktopText.GitDiffFailed);
        viewModel.AddLog(result.Succeeded
            ? Ui(viewModel, DesktopText.GitDiffRefreshed)
            : $"{Ui(viewModel, DesktopText.GitDiffFailed)}: {trimForLog(result.DisplayOutput)}");
    }

    public async Task LoadSelectedFileDiffAsync(MainViewModel viewModel, CancellationToken ct = default)
    {
        if (viewModel.SelectedGitChangedFile == null)
        {
            viewModel.GitSelectedFileDiffText = Ui(viewModel, DesktopText.GitSelectedFileEmpty);
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitLoadingFileDiff);
        var result = await gitService.GetFileDiffAsync(viewModel.WorkspaceRoot, viewModel.SelectedGitChangedFile, ct);
        viewModel.GitSelectedFileDiffText = result.DisplayOutput;
        viewModel.GitLastUpdatedText = CurrentTimestamp(viewModel);
        viewModel.StatusText = result.Succeeded ? Ui(viewModel, DesktopText.GitFileDiffLoaded) : Ui(viewModel, DesktopText.GitFileDiffFailed);
    }

    public async Task StageSelectedFileAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitNoSelectedChangedFile);
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitStagingSelectedFile);
        var result = await gitService.StageFileAsync(viewModel.WorkspaceRoot, file, ct);
        await ApplyGitMutationResultAsync(
            viewModel,
            result,
            Ui(viewModel, DesktopText.GitSelectedFileStaged),
            Ui(viewModel, DesktopText.GitStageSelectedFailed),
            trimForLog,
            ct);
    }

    public async Task StageApprovedFilesAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var files = viewModel.GitChangedFiles
            .Where(file => file.ReviewStatus == GitChangeReviewStatus.Approved)
            .ToArray();

        viewModel.StatusText = Ui(viewModel, DesktopText.GitStagingApprovedFiles);
        var result = await gitService.StageFilesAsync(viewModel.WorkspaceRoot, files, ct);
        await ApplyGitMutationResultAsync(
            viewModel,
            result,
            Ui(viewModel, DesktopText.GitApprovedFilesStaged),
            Ui(viewModel, DesktopText.GitStageApprovedFailed),
            trimForLog,
            ct);
    }

    public async Task UnstageSelectedFileAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitNoSelectedChangedFile);
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitUnstagingSelectedFile);
        var result = await gitService.UnstageFileAsync(viewModel.WorkspaceRoot, file, ct);
        await ApplyGitMutationResultAsync(
            viewModel,
            result,
            Ui(viewModel, DesktopText.GitSelectedFileUnstaged),
            Ui(viewModel, DesktopText.GitUnstageSelectedFailed),
            trimForLog,
            ct);
    }

    public async Task CommitAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewModel.GitCommitMessage))
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitCommitMessageRequired);
            return;
        }

        if (viewModel.GitChangedFiles.All(file => !file.IsStaged))
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitNoStagedFilesToCommit);
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitCreatingCommit);
        var result = await gitService.CommitAsync(viewModel.WorkspaceRoot, viewModel.GitCommitMessage.Trim(), ct);
        if (result.Succeeded)
        {
            viewModel.GitCommitMessage = string.Empty;
        }

        await ApplyGitMutationResultAsync(viewModel, result, Ui(viewModel, DesktopText.GitCommitCreated), Ui(viewModel, DesktopText.GitCommitFailed), trimForLog, ct);
    }

    public async Task PullFastForwardOnlyAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = Ui(viewModel, DesktopText.GitCheckingPullSafety);
        var snapshot = await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct);
        ApplySnapshot(viewModel, snapshot);

        if (!snapshot.Status.Succeeded)
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitPullUnavailable, trimForLog(snapshot.Status.DisplayOutput));
            return;
        }

        var safety = GitPullSafetyAnalyzer.Analyze(snapshot.Status.DisplayOutput, snapshot.ChangedFiles);
        if (!safety.CanPull)
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitPullBlocked, safety.Reason);
            viewModel.AddLog(Ui(viewModel, DesktopText.GitPullBlocked, safety.Reason));
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitPullingFastForward);
        var result = await gitService.PullFastForwardOnlyAsync(viewModel.WorkspaceRoot, ct);
        await ApplyGitMutationResultAsync(viewModel, result, Ui(viewModel, DesktopText.GitPullCompleted), Ui(viewModel, DesktopText.GitPullFailed), trimForLog, ct);
    }

    public async Task CreateBackupBranchAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = Ui(viewModel, DesktopText.GitCreatingBackupBranch);
        var branchName = GitBranchRecoveryAnalyzer.CreateBackupBranchName(DateTime.Now);
        var result = await gitService.CreateBranchAsync(viewModel.WorkspaceRoot, branchName, ct);
        await ApplyGitMutationResultAsync(
            viewModel,
            result,
            Ui(viewModel, DesktopText.GitBackupBranchCreated, branchName),
            Ui(viewModel, DesktopText.GitBackupBranchFailed),
            trimForLog,
            ct);
    }

    public async Task CheckoutMainAsync(MainViewModel viewModel, Func<string, string> trimForLog, CancellationToken ct = default)
    {
        viewModel.StatusText = Ui(viewModel, DesktopText.GitCheckingBranchSwitchSafety);
        var snapshot = await _gitWorkflowService.GetSnapshotAsync(viewModel.WorkspaceRoot, ct);
        ApplySnapshot(viewModel, snapshot);

        if (!GitBranchRecoveryAnalyzer.CanSwitchBranch(snapshot.ChangedFiles, out var reason))
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitSwitchBlocked, reason);
            viewModel.AddLog(Ui(viewModel, DesktopText.GitSwitchBlocked, reason));
            return;
        }

        viewModel.StatusText = Ui(viewModel, DesktopText.GitSwitchingToMain);
        var result = await gitService.CheckoutBranchAsync(viewModel.WorkspaceRoot, "main", ct);
        await ApplyGitMutationResultAsync(viewModel, result, Ui(viewModel, DesktopText.GitSwitchedToMain), Ui(viewModel, DesktopText.GitSwitchToMainFailed), trimForLog, ct);
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
        var recoveryAdvice = GitBranchRecoveryAnalyzer.BuildRecoveryAdvice(snapshot.Status.DisplayOutput, snapshot.ChangedFiles);
        viewModel.GitStatusText = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { snapshot.Status.DisplayOutput, branchSummary, recoveryAdvice }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        viewModel.GitDiffText = snapshot.DiffStat.DisplayOutput;
        ApplyChangedFiles(viewModel, snapshot.ChangedFiles, selectedPath);
        viewModel.GitLastUpdatedText = CurrentTimestamp(viewModel);
    }

    public void SetSelectedReviewStatus(MainViewModel viewModel, GitChangeReviewStatus status)
    {
        var file = viewModel.SelectedGitChangedFile;
        if (file == null)
        {
            viewModel.StatusText = Ui(viewModel, DesktopText.GitNoSelectedChangedFile);
            return;
        }

        file.ReviewStatus = status;
        _reviewStatuses[file.Path] = status;
        viewModel.StatusText = Ui(viewModel, DesktopText.GitChangeMarked, status);
        viewModel.AddLog(Ui(viewModel, DesktopText.GitChangeReviewStatus, status, file.Path));
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
        viewModel.AddLog(Ui(viewModel, DesktopText.GitCodeReviewCaptured));
    }

    public void ClearLastCodeReview(MainViewModel viewModel)
    {
        _lastCodeReviewText = string.Empty;
        viewModel.CanFixLastCodeReviewFindings = false;
    }

    public void ClearPanel(MainViewModel viewModel)
    {
        _reviewStatuses.Clear();
        viewModel.Git.Reset();
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
            viewModel.GitSelectedFileDiffText = Ui(viewModel, DesktopText.GitNoChangedFiles);
        }
    }

    private static string CurrentTimestamp(MainViewModel viewModel)
    {
        return Ui(
            viewModel,
            DesktopText.GitLastUpdated,
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private static string Ui(MainViewModel viewModel, string key)
    {
        return DesktopLocalizer.UiText(key, viewModel.IsKoreanUi);
    }

    private static string Ui(MainViewModel viewModel, string key, params object[] args)
    {
        return DesktopLocalizer.FormatUiText(key, viewModel.IsKoreanUi, args);
    }
}
